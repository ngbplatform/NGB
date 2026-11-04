import { createRouter, createWebHistory } from 'vue-router'
import { createAuthGuard, useAuthStore } from '@ngbplatform/ui'
import {
  loadNgbRolesPage,
  loadNgbRoleEditorPage,
  loadNgbUsersPage,
  loadNgbUserEditorPage,
  loadNgbWorkCenterPage,
  loadNgbNotificationPreferencesPage,
} from '@ngbplatform/ui/lazy'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/admin/security/roles' },
    { path: '/admin/security/roles', component: loadNgbRolesPage },
    { path: '/admin/security/roles/:roleId', component: loadNgbRoleEditorPage },
    { path: '/admin/security/users', component: loadNgbUsersPage },
    { path: '/admin/security/users/:userId', component: loadNgbUserEditorPage },
    { path: '/work-center', component: loadNgbWorkCenterPage },
    { path: '/settings/notifications', component: loadNgbNotificationPreferencesPage },
  ],
})

router.beforeEach(createAuthGuard(() => useAuthStore()))
