import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { BarChart3, Bell, FileArchive, FileUp, Home, Landmark, NotebookPen, Repeat, Settings, Tags, WalletCards } from 'lucide-react';
import { useEffect, useState } from 'react';
import { AccountsPage } from './pages/AccountsPage';
import { DashboardPage } from './pages/DashboardPage';
import { ImportsPage } from './pages/ImportsPage';
import { RulesetPage } from './pages/RulesetPage';
import { ReportsPage } from './pages/ReportsPage';
import { SettingsPage } from './pages/SettingsPage';
import { StoragePage } from './pages/StoragePage';
import { SubscriptionsPage } from './pages/SubscriptionsPage';
import { TransactionsPage } from './pages/TransactionsPage';
import { TagsPage } from './pages/TagsPage';
import { NotesPage } from './pages/NotesPage';
import { listReminders } from './services/organizationService';

const navItems = [
  { to: '/dashboard', label: 'Dashboard', icon: Home },
  { to: '/accounts', label: 'Accounts', icon: Landmark },
  { to: '/transactions', label: 'Transactions', icon: WalletCards },
  { to: '/imports', label: 'Imports', icon: FileUp },
  { to: '/storage', label: 'Storage', icon: FileArchive },
  { to: '/subscriptions', label: 'Subscriptions', icon: Repeat },
  { to: '/tags', label: 'Tags', icon: Tags },
  { to: '/notes', label: 'Notes', icon: NotebookPen },
  { to: '/reports', label: 'Reports', icon: BarChart3 },
  { to: '/settings', label: 'Settings', icon: Settings },
];

function App() {
  const [pendingReminders, setPendingReminders] = useState(0);
  useEffect(() => {
    listReminders().then((items) => setPendingReminders(items.length)).catch(() => setPendingReminders(0));
  }, []);

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">Finance Tracker</div>
        <nav className="nav-list" aria-label="Primary">
          {navItems.map((item) => {
            const Icon = item.icon;
            return (
              <NavLink key={item.to} to={item.to} className="nav-item">
                <Icon aria-hidden="true" size={18} />
                <span>{item.label}</span>
                {item.to === '/dashboard' && pendingReminders > 0 && <span className="nav-badge"><Bell size={12} />{pendingReminders}</span>}
              </NavLink>
            );
          })}
        </nav>
      </aside>
      <main className="main-panel">
        <Routes>
          <Route path="/" element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/accounts" element={<AccountsPage />} />
          <Route path="/accounts/:id" element={<AccountsPage />} />
          <Route path="/transactions" element={<TransactionsPage />} />
          <Route path="/imports" element={<ImportsPage />} />
          <Route path="/imports/rulesets/:rulesetId" element={<RulesetPage />} />
          <Route path="/storage" element={<StoragePage />} />
          <Route path="/subscriptions" element={<SubscriptionsPage />} />
          <Route path="/tags" element={<TagsPage />} />
          <Route path="/notes" element={<NotesPage />} />
          <Route path="/reports" element={<ReportsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
      </main>
    </div>
  );
}

export default App;
