import { Button, InlineNotification, Select, SelectItem, TextInput } from '@carbon/react';
import { useState } from 'react';
import type { ApiClient } from '../api/client';
import { TENANTS, type Session } from '../session';

export function LoginPage({ api, onSignIn }: { api: ApiClient; onSignIn: (s: Session) => void }) {
  const [tenant, setTenant] = useState(TENANTS[0].id);
  const [user, setUser] = useState('operator');
  const [error, setError] = useState('');

  const submit = async () => {
    try {
      // Tenant header is not in the session yet, so the login call is anonymous.
      const res = await api.api.auth.token.post({ tenant, user });
      onSignIn({ tenant, user, token: res?.token ?? '' });
    } catch {
      setError('Sign-in failed');
    }
  };

  return (
    <div className="login">
      <h1>Grid Meter Cloud</h1>
      <Select id="tenant" labelText="Tenant" value={tenant} onChange={(e) => setTenant(e.target.value)}>
        {TENANTS.map((t) => (
          <SelectItem key={t.id} value={t.id} text={t.name} />
        ))}
      </Select>
      <TextInput id="user" labelText="User" value={user} onChange={(e) => setUser(e.target.value)} />
      <Button onClick={submit}>Sign in</Button>
      {error && <InlineNotification kind="error" title={error} hideCloseButton />}
    </div>
  );
}
