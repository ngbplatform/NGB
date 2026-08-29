-- Supports the stable display-name ordering used by the paged platform-user read path.
CREATE INDEX IF NOT EXISTS ix_platform_users_display_sort
    ON public.platform_users(lower(coalesce(display_name, email, auth_subject)), user_id);
