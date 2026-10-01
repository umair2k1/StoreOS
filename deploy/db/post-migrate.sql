GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO storeos_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO storeos_app;
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY['audit_log','customer_ledger_entries','supplier_ledger_entries',
      'inventory_movements','repair_job_status_history','customer_payments','liability_payments'] LOOP
    IF to_regclass('public.' || t) IS NOT NULL THEN
      EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON public.%I FROM storeos_app', t);
    END IF;
  END LOOP;
  IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
    REVOKE ALL ON public."__EFMigrationsHistory" FROM storeos_app;
  END IF;
END $$;
