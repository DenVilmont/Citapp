create extension if not exists pgcrypto;

create table if not exists profiles (
  user_id uuid primary key references auth.users(id) on delete cascade,
  email text,
  display_name text,
  created_at timestamptz default now()
);

create table if not exists tenants (
  id uuid primary key default gen_random_uuid(),
  owner_user_id uuid not null references auth.users(id) on delete cascade,
  business_name text not null,
  about_text text,
  greeting_text text not null,
  timezone text not null default 'Europe/Madrid',
  currency text not null default 'EUR',
  slot_step_minutes int not null default 15,
  default_buffer_minutes int not null default 0,
  booking_enabled boolean not null default true,
  created_at timestamptz not null default now()
);

create table if not exists tenant_members (
  tenant_id uuid not null references tenants(id) on delete cascade,
  user_id uuid not null references auth.users(id) on delete cascade,
  role text not null default 'owner',
  primary key (tenant_id, user_id)
);

create table if not exists whatsapp_connections (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  waba_id text,
  phone_number_id text not null,
  phone_number text not null,
  display_name text,
  status text not null default 'active',
  created_at timestamptz not null default now(),
  connected_at timestamptz
);

create table if not exists services (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  name text not null,
  description text,
  duration_minutes int not null,
  price_amount numeric not null,
  currency text not null,
  is_active boolean not null default true,
  sort_order int not null default 0,
  created_at timestamptz not null default now()
);

create table if not exists service_media (
  id uuid primary key default gen_random_uuid(),
  service_id uuid not null references services(id) on delete cascade,
  storage_path text not null,
  public_url text,
  is_primary boolean not null default false,
  sort_order int not null default 0
);

create table if not exists weekly_working_hours (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  day_of_week int not null,
  start_time time not null,
  end_time time not null,
  is_working_day boolean not null default true
);

create table if not exists fixed_breaks (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  day_of_week int not null,
  start_time time not null,
  end_time time not null,
  label text
);

create table if not exists blocked_dates (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  date date not null,
  start_time time,
  end_time time,
  reason text
);

create table if not exists customers (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  wa_user_id text not null,
  phone text,
  display_name text not null default 'Клиент',
  master_note text,
  created_at timestamptz not null default now(),
  last_seen_at timestamptz,
  unique (tenant_id, wa_user_id)
);

create table if not exists bookings (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  customer_id uuid not null references customers(id) on delete restrict,
  service_id uuid not null references services(id) on delete restrict,
  source text not null,
  status text not null default 'booked',
  date date not null,
  start_at timestamptz not null,
  end_at timestamptz not null,
  duration_snapshot_minutes int not null,
  price_snapshot_amount numeric not null,
  currency_snapshot text not null,
  created_by_user_id uuid,
  cancelled_by text,
  cancelled_at timestamptz,
  created_at timestamptz not null default now()
);

create index if not exists idx_bookings_tenant_date on bookings(tenant_id, date);
create unique index if not exists ux_bookings_active_service_per_customer
on bookings(tenant_id, customer_id, service_id)
where status = 'booked' and start_at > now();

create table if not exists conversation_states (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references tenants(id) on delete cascade,
  customer_id uuid not null references customers(id) on delete cascade,
  state text not null,
  payload_json jsonb,
  updated_at timestamptz not null default now(),
  expires_at timestamptz not null,
  unique (tenant_id, customer_id)
);

create table if not exists inbound_webhook_events (
  id uuid primary key default gen_random_uuid(),
  provider text not null default 'whatsapp',
  external_event_id text unique,
  tenant_id uuid references tenants(id) on delete set null,
  direction text not null,
  payload_json jsonb not null,
  processed boolean not null default false,
  created_at timestamptz not null default now()
);
