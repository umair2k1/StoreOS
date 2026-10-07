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

CREATE OR REPLACE FUNCTION public.delete_tenant_data_for_restore(p_tenant_id uuid)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    DELETE FROM public.repair_job_status_history WHERE tenant_id = p_tenant_id;
    DELETE FROM public.repair_job_parts WHERE tenant_id = p_tenant_id;
    DELETE FROM public.repair_jobs WHERE tenant_id = p_tenant_id;
    DELETE FROM public.customer_payments WHERE tenant_id = p_tenant_id;
    DELETE FROM public.sale_items WHERE tenant_id = p_tenant_id;
    DELETE FROM public.customer_ledger_entries WHERE tenant_id = p_tenant_id;
    DELETE FROM public.sales WHERE tenant_id = p_tenant_id;
    DELETE FROM public.customers WHERE tenant_id = p_tenant_id;
    DELETE FROM public.supplier_return_items WHERE tenant_id = p_tenant_id;
    DELETE FROM public.supplier_returns WHERE tenant_id = p_tenant_id;
    DELETE FROM public.supplier_ledger_entries WHERE tenant_id = p_tenant_id;
    DELETE FROM public.supplier_transactions WHERE tenant_id = p_tenant_id;
    DELETE FROM public.suppliers WHERE tenant_id = p_tenant_id;
    DELETE FROM public.liability_payments WHERE tenant_id = p_tenant_id;
    DELETE FROM public.liabilities WHERE tenant_id = p_tenant_id;
    DELETE FROM public.expenses WHERE tenant_id = p_tenant_id;
    DELETE FROM public.inventory_movements WHERE tenant_id = p_tenant_id;
    DELETE FROM public.inventory_imeis WHERE tenant_id = p_tenant_id;
    DELETE FROM public.purchases WHERE tenant_id = p_tenant_id;
    DELETE FROM public.branch_inventory WHERE tenant_id = p_tenant_id;
    DELETE FROM public.products WHERE tenant_id = p_tenant_id;
    DELETE FROM public.refresh_tokens WHERE user_id IN (SELECT id FROM public.users WHERE tenant_id = p_tenant_id);
    DELETE FROM public.user_role_assignments WHERE user_id IN (SELECT id FROM public.users WHERE tenant_id = p_tenant_id);
    DELETE FROM public.user_branches WHERE user_id IN (SELECT id FROM public.users WHERE tenant_id = p_tenant_id);
    DELETE FROM public.role_permissions WHERE role_id IN (SELECT id FROM public.roles WHERE tenant_id = p_tenant_id);
    DELETE FROM public.roles WHERE tenant_id = p_tenant_id;
    DELETE FROM public.users WHERE tenant_id = p_tenant_id;
    DELETE FROM public.settings WHERE tenant_id = p_tenant_id;
    DELETE FROM public.branches WHERE tenant_id = p_tenant_id;
END;
$$;

GRANT EXECUTE ON FUNCTION public.delete_tenant_data_for_restore(uuid) TO storeos_app;

CREATE SCHEMA IF NOT EXISTS hangfire;
GRANT ALL ON SCHEMA hangfire TO storeos_app;
