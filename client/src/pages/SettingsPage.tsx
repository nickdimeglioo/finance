import { FormEvent, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, EmptyState, Input, Select, Table, type TableColumn } from '../components/ui';
import { displayEnum } from '../lib/display';
import { listCategories, renameCategory, getProfile, updateProfile } from '../services/settingsService';
import type { CategoryDto, UpdateUserFinanceProfileRequest, UserFinanceProfileDto } from '../types/schema';

type SettingsTab = 'profile' | 'categories' | 'links';

export function SettingsPage() {
  const [tab, setTab] = useState<SettingsTab>('profile');

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <h1 className="page-title">Settings</h1>
          <p className="page-subtitle">Finance profile, category mappings, and configuration shortcuts.</p>
        </div>
      </header>

      <div className="tabs">
        {(['profile', 'categories', 'links'] as SettingsTab[]).map((item) => (
          <button key={item} type="button" className={tab === item ? 'tab active' : 'tab'} onClick={() => setTab(item)}>
            {displayEnum(item)}
          </button>
        ))}
      </div>

      {tab === 'profile' && <ProfileSettings />}
      {tab === 'categories' && <CategorySettings />}
      {tab === 'links' && <SettingsLinks />}
    </section>
  );
}

function ProfileSettings() {
  const [profile, setProfile] = useState<UserFinanceProfileDto | null>(null);
  const [goalText, setGoalText] = useState('');
  const [mappingCategory, setMappingCategory] = useState('');
  const [mappingBucket, setMappingBucket] = useState<'needs' | 'wants' | 'savings'>('needs');
  const [message, setMessage] = useState<string | null>(null);

  useEffect(() => {
    getProfile().then(setProfile).catch((err: Error) => setMessage(err.message));
  }, []);

  if (!profile) {
    return <div className="panel">{message ?? 'Loading profile...'}</div>;
  }

  async function save(next: UpdateUserFinanceProfileRequest) {
    setMessage(null);
    try {
      setProfile(await updateProfile(next));
      setMessage('Profile saved.');
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to save profile.');
    }
  }

  const request: UpdateUserFinanceProfileRequest = {
    dateOfBirth: profile.dateOfBirth ?? null,
    annualIncome: profile.annualIncome ?? null,
    incomeType: profile.incomeType,
    dependents: profile.dependents,
    financialGoals: profile.financialGoals,
    categoryMappings: profile.categoryMappings,
  };

  return (
    <div className="split-panel">
      {message && <div className="notice">{message}</div>}
      <form className="panel form-grid" onSubmit={(event) => { event.preventDefault(); void save(request); }}>
        <Input label="Date of birth" type="date" value={profile.dateOfBirth ?? ''} onChange={(event) => setProfile({ ...profile, dateOfBirth: event.target.value || null })} />
        <Input label="Annual income" type="number" step="0.01" min="0" value={profile.annualIncome ?? ''} onChange={(event) => setProfile({ ...profile, annualIncome: event.target.value ? Number(event.target.value) : null })} />
        <Select label="Income type" value={profile.incomeType} onChange={(event) => setProfile({ ...profile, incomeType: event.target.value as UserFinanceProfileDto['incomeType'] })}>
          {['salaried', 'freelance', 'mixed', 'retired', 'student', 'other'].map((type) => <option key={type} value={type}>{displayEnum(type)}</option>)}
        </Select>
        <Input label="Dependents" type="number" min="0" value={profile.dependents} onChange={(event) => setProfile({ ...profile, dependents: Number(event.target.value) })} />
        <Button type="submit">Save Profile</Button>
      </form>

      <div className="panel">
        <div className="panel-header"><h2>Financial Goals</h2></div>
        <div className="inline-actions">
          <Input label="Goal" value={goalText} onChange={(event) => setGoalText(event.target.value)} />
          <Button type="button" onClick={() => {
            if (!goalText.trim()) return;
            void save({ ...request, financialGoals: [...profile.financialGoals, goalText.trim()] });
            setGoalText('');
          }}>Add</Button>
        </div>
        {profile.financialGoals.length === 0 ? <EmptyState title="No goals set." /> : (
          <div className="tag-list">
            {profile.financialGoals.map((goal) => <span key={goal} className="tag-chip">{goal}</span>)}
          </div>
        )}
      </div>

      <div className="panel">
        <div className="panel-header"><h2>50/30/20 Category Mapping</h2></div>
        <div className="compact-form">
          <Input label="Category" value={mappingCategory} onChange={(event) => setMappingCategory(event.target.value)} />
          <Select label="Bucket" value={mappingBucket} onChange={(event) => setMappingBucket(event.target.value as 'needs' | 'wants' | 'savings')}>
            <option value="needs">Needs</option>
            <option value="wants">Wants</option>
            <option value="savings">Savings</option>
          </Select>
          <Button type="button" onClick={() => {
            if (!mappingCategory.trim()) return;
            void save({ ...request, categoryMappings: { ...profile.categoryMappings, [mappingCategory.trim()]: mappingBucket } });
            setMappingCategory('');
          }}>Map</Button>
        </div>
        {Object.keys(profile.categoryMappings).length === 0 ? <EmptyState title="No category mappings yet." /> : (
          <div className="tag-list">
            {Object.entries(profile.categoryMappings).map(([category, bucket]) => <span key={category} className="tag-chip">{category}: {bucket}</span>)}
          </div>
        )}
      </div>
    </div>
  );
}

function CategorySettings() {
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [message, setMessage] = useState<string | null>(null);

  async function load() {
    setCategories(await listCategories());
  }

  useEffect(() => {
    load().catch((err: Error) => setMessage(err.message));
  }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    try {
      const result = await renameCategory(from, to);
      setMessage(`Updated ${result.updated} rows.`);
      setFrom('');
      setTo('');
      await load();
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Unable to rename category.');
    }
  }

  const columns: TableColumn<CategoryDto>[] = [
    { key: 'name', label: 'Category' },
    { key: 'transactionCount', label: 'Transactions', align: 'right' },
    { key: 'splitCount', label: 'Splits', align: 'right' },
    { key: 'actions', label: '', align: 'right', render: (category) => <Button type="button" size="sm" variant="secondary" onClick={() => setFrom(category.name)}>Rename</Button> },
  ];

  return (
    <div className="split-panel">
      {message && <div className="notice">{message}</div>}
      <form className="panel form-grid" onSubmit={submit}>
        <Input label="Rename from" value={from} onChange={(event) => setFrom(event.target.value)} required />
        <Input label="Rename to / merge into" value={to} onChange={(event) => setTo(event.target.value)} required />
        <Button type="submit">Apply</Button>
      </form>
      <div className="panel">
        <div className="panel-header"><h2>Categories</h2></div>
        <Table data={categories} columns={columns} rowKey="name" emptyMessage="No categories found in transactions or splits." />
      </div>
    </div>
  );
}

function SettingsLinks() {
  const links = [
    { to: '/tags', label: 'Tags Manager' },
    { to: '/imports', label: 'Imports and Rulesets' },
    { to: '/subscriptions', label: 'Recurring Rules / Subscriptions' },
    { to: '/accounts', label: 'Account Settings' },
    { to: '/reports', label: 'Data / Exports' },
  ];
  return (
    <div className="panel settings-link-grid">
      {links.map((link) => <Link key={link.to} className="settings-link" to={link.to}>{link.label}</Link>)}
    </div>
  );
}

