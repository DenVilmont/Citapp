alter table tenants enable row level security;
create policy tenant_owner on tenants using (owner_user_id = auth.uid());

alter table whatsapp_connections enable row level security;
create policy whatsapp_connections_owner on whatsapp_connections using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table services enable row level security;
create policy services_owner on services using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table service_media enable row level security;
create policy service_media_owner on service_media using (service_id in (select s.id from services s join tenants t on t.id = s.tenant_id where t.owner_user_id = auth.uid()));

alter table weekly_working_hours enable row level security;
create policy weekly_working_hours_owner on weekly_working_hours using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table fixed_breaks enable row level security;
create policy fixed_breaks_owner on fixed_breaks using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table blocked_dates enable row level security;
create policy blocked_dates_owner on blocked_dates using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table customers enable row level security;
create policy customers_owner on customers using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table bookings enable row level security;
create policy bookings_owner on bookings using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table tenant_members enable row level security;
create policy tenant_members_owner on tenant_members using (tenant_id in (select id from tenants where owner_user_id = auth.uid()));

alter table profiles enable row level security;
create policy profiles_owner on profiles using (user_id = auth.uid());
