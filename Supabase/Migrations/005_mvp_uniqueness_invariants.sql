-- MVP integrity hardening: enforce one-row uniqueness invariants.

-- 1) One weekly schedule row per tenant/day.
do $$
begin
  if exists (
    select 1
    from weekly_working_hours w
    group by w.tenant_id, w.day_of_week
    having count(*) > 1
  ) then
    raise exception using
      errcode = '23505',
      message = 'Cannot enforce uniqueness on weekly_working_hours(tenant_id, day_of_week): duplicate rows exist';
  end if;
end
$$;

create unique index if not exists ux_weekly_working_hours_tenant_day
  on weekly_working_hours (tenant_id, day_of_week);

-- 2) One primary image per service.
do $$
begin
  if exists (
    select 1
    from service_media sm
    where sm.is_primary is true
    group by sm.service_id
    having count(*) > 1
  ) then
    raise exception using
      errcode = '23505',
      message = 'Cannot enforce single primary media per service: multiple is_primary=true rows exist for at least one service';
  end if;
end
$$;

create unique index if not exists ux_service_media_one_primary_per_service
  on service_media (service_id)
  where is_primary is true;

-- 3) One WhatsApp connection per tenant (MVP assumption).
do $$
begin
  if exists (
    select 1
    from whatsapp_connections wc
    group by wc.tenant_id
    having count(*) > 1
  ) then
    raise exception using
      errcode = '23505',
      message = 'Cannot enforce one whatsapp connection per tenant: duplicate tenant_id rows exist';
  end if;
end
$$;

create unique index if not exists ux_whatsapp_connections_tenant
  on whatsapp_connections (tenant_id);
