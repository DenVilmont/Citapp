-- 1) Complete RLS write protection (owner pattern + WITH CHECK)
alter policy tenant_owner on tenants
  using (owner_user_id = auth.uid())
  with check (owner_user_id = auth.uid());

alter policy whatsapp_connections_owner on whatsapp_connections
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy services_owner on services
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy service_media_owner on service_media
  using (
    service_id in (
      select s.id
      from services s
      join tenants t on t.id = s.tenant_id
      where t.owner_user_id = auth.uid()
    )
  )
  with check (
    service_id in (
      select s.id
      from services s
      join tenants t on t.id = s.tenant_id
      where t.owner_user_id = auth.uid()
    )
  );

alter policy weekly_working_hours_owner on weekly_working_hours
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy fixed_breaks_owner on fixed_breaks
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy blocked_dates_owner on blocked_dates
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy customers_owner on customers
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy bookings_owner on bookings
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy tenant_members_owner on tenant_members
  using (tenant_id in (select id from tenants where owner_user_id = auth.uid()))
  with check (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter policy profiles_owner on profiles
  using (user_id = auth.uid())
  with check (user_id = auth.uid());

-- 2) Explicit backend-only table lock down (browser roles: anon/authenticated)
revoke all on table conversation_states from anon, authenticated;
revoke all on table inbound_webhook_events from anon, authenticated;

alter table conversation_states enable row level security;
alter table conversation_states force row level security;

alter table inbound_webhook_events enable row level security;
alter table inbound_webhook_events force row level security;

drop policy if exists conversation_states_deny_browser on conversation_states;
create policy conversation_states_deny_browser
  on conversation_states
  as restrictive
  for all
  to anon, authenticated
  using (false)
  with check (false);

drop policy if exists inbound_webhook_events_deny_browser on inbound_webhook_events;
create policy inbound_webhook_events_deny_browser
  on inbound_webhook_events
  as restrictive
  for all
  to anon, authenticated
  using (false)
  with check (false);

-- 3) Constraints/invariants hardening
alter table services
  add constraint ck_services_duration_positive
  check (duration_minutes > 0) not valid;

alter table services
  add constraint ck_services_price_non_negative
  check (price_amount >= 0) not valid;

alter table weekly_working_hours
  add constraint ck_weekly_working_hours_day_of_week
  check (day_of_week between 0 and 6) not valid;

alter table weekly_working_hours
  add constraint ck_weekly_working_hours_time_window
  check (start_time < end_time) not valid;

alter table fixed_breaks
  add constraint ck_fixed_breaks_day_of_week
  check (day_of_week between 0 and 6) not valid;

alter table fixed_breaks
  add constraint ck_fixed_breaks_time_window
  check (start_time < end_time) not valid;

alter table blocked_dates
  add constraint ck_blocked_dates_interval_or_full_day
  check (
    (start_time is null and end_time is null)
    or (start_time is not null and end_time is not null and start_time < end_time)
  ) not valid;

alter table bookings
  add constraint ck_bookings_time_window
  check (start_at < end_at) not valid;

alter table bookings
  add constraint ck_bookings_duration_snapshot_positive
  check (duration_snapshot_minutes > 0) not valid;

alter table bookings
  add constraint ck_bookings_price_snapshot_non_negative
  check (price_snapshot_amount >= 0) not valid;

alter table bookings
  add constraint ck_bookings_status
  check (status in ('booked', 'cancelled', 'completed', 'no_show')) not valid;

alter table bookings
  add constraint ck_bookings_source
  check (source in ('chat', 'admin')) not valid;

alter table bookings
  add constraint ck_bookings_cancelled_by
  check (cancelled_by is null or cancelled_by in ('customer', 'master', 'system')) not valid;

-- 4) Missing uniqueness/consistency
alter table whatsapp_connections
  add constraint uq_whatsapp_connections_phone_number_id unique (phone_number_id);

-- 5) Replace volatile-time partial unique index with robust trigger enforcement
-- Preserve business rule: one customer cannot have more than one active future booking
-- for the same service in the same tenant.
drop index if exists ux_bookings_active_service_per_customer;

create or replace function public.enforce_active_future_booking_uniqueness()
returns trigger
language plpgsql
as $$
begin
  if new.status = 'booked' and new.start_at > now() then
    perform pg_advisory_xact_lock(
      hashtextextended(new.tenant_id::text || '|' || new.customer_id::text || '|' || new.service_id::text, 0)
    );

    if exists (
      select 1
      from bookings b
      where b.tenant_id = new.tenant_id
        and b.customer_id = new.customer_id
        and b.service_id = new.service_id
        and b.status = 'booked'
        and b.start_at > now()
        and b.id <> coalesce(new.id, '00000000-0000-0000-0000-000000000000'::uuid)
    ) then
      raise exception using
        errcode = '23505',
        message = 'Active future booking already exists for this tenant/customer/service';
    end if;
  end if;

  return new;
end;
$$;

drop trigger if exists trg_enforce_active_future_booking_uniqueness on bookings;
create trigger trg_enforce_active_future_booking_uniqueness
before insert or update of tenant_id, customer_id, service_id, status, start_at
on bookings
for each row
execute function public.enforce_active_future_booking_uniqueness();

-- 6) Explicit service media storage setup
insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'service-media',
  'service-media',
  true,
  10485760,
  array['image/jpeg', 'image/png', 'image/webp', 'image/gif']
)
on conflict (id) do update
set
  name = excluded.name,
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

create or replace function public.storage_path_tenant_uuid(object_name text)
returns uuid
language plpgsql
stable
as $$
declare
  tenant_part text;
begin
  tenant_part := split_part(object_name, '/', 1);

  if tenant_part ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$' then
    return tenant_part::uuid;
  end if;

  return null;
end;
$$;

drop policy if exists service_media_public_read on storage.objects;
create policy service_media_public_read
  on storage.objects
  for select
  using (bucket_id = 'service-media');

drop policy if exists service_media_owner_insert on storage.objects;
create policy service_media_owner_insert
  on storage.objects
  for insert
  to authenticated
  with check (
    bucket_id = 'service-media'
    and exists (
      select 1
      from tenants t
      where t.id = public.storage_path_tenant_uuid(name)
        and t.owner_user_id = auth.uid()
    )
  );

drop policy if exists service_media_owner_update on storage.objects;
create policy service_media_owner_update
  on storage.objects
  for update
  to authenticated
  using (
    bucket_id = 'service-media'
    and exists (
      select 1
      from tenants t
      where t.id = public.storage_path_tenant_uuid(name)
        and t.owner_user_id = auth.uid()
    )
  )
  with check (
    bucket_id = 'service-media'
    and exists (
      select 1
      from tenants t
      where t.id = public.storage_path_tenant_uuid(name)
        and t.owner_user_id = auth.uid()
    )
  );

drop policy if exists service_media_owner_delete on storage.objects;
create policy service_media_owner_delete
  on storage.objects
  for delete
  to authenticated
  using (
    bucket_id = 'service-media'
    and exists (
      select 1
      from tenants t
      where t.id = public.storage_path_tenant_uuid(name)
        and t.owner_user_id = auth.uid()
    )
  );
