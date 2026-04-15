alter table bookings
  drop constraint if exists ck_bookings_status;

alter table bookings
  add constraint ck_bookings_status
  check (status in ('booked', 'blocked_by_master', 'cancelled', 'completed', 'no_show')) not valid;
