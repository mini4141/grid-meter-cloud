import { Button, Header, HeaderGlobalBar, HeaderName, HeaderNavigation, HeaderMenuItem, Tag } from '@carbon/react';
import { useState } from 'react';
import { Link, Navigate, Route, Routes, useNavigate } from 'react-router-dom';
import type { ApiClient } from './api/client';
import { AdminPage } from './pages/AdminPage';
import { LoginPage } from './pages/LoginPage';
import { SitePage } from './pages/SitePage';
import { SitesPage } from './pages/SitesPage';
import { loadSession, saveSession, type Session } from './session';

export function App({ api }: { api: ApiClient }) {
  const [session, setSession] = useState<Session | null>(loadSession());
  const navigate = useNavigate();

  const signIn = (s: Session) => {
    saveSession(s);
    setSession(s);
    navigate('/');
  };
  const signOut = () => {
    saveSession(null);
    setSession(null);
    navigate('/login');
  };

  if (!session) {
    return (
      <Routes>
        <Route path="*" element={<LoginPage api={api} onSignIn={signIn} />} />
      </Routes>
    );
  }

  return (
    <>
      <Header aria-label="Grid Meter Cloud">
        <HeaderName as={Link} to="/" prefix="Grid Meter">
          Cloud
        </HeaderName>
        <HeaderNavigation aria-label="Main">
          <HeaderMenuItem as={Link} to="/">
            Sites
          </HeaderMenuItem>
          <HeaderMenuItem as={Link} to="/admin">
            Admin
          </HeaderMenuItem>
        </HeaderNavigation>
        <HeaderGlobalBar>
          <Tag type="blue" data-testid="tenant-tag">
            {session.tenant}
          </Tag>
          <Button kind="ghost" size="lg" onClick={signOut}>
            Switch tenant
          </Button>
        </HeaderGlobalBar>
      </Header>
      <main className="page">
        <Routes>
          <Route path="/" element={<SitesPage api={api} />} />
          <Route path="/sites/:id" element={<SitePage api={api} />} />
          <Route path="/admin" element={<AdminPage api={api} />} />
          <Route path="*" element={<Navigate to="/" />} />
        </Routes>
      </main>
    </>
  );
}
