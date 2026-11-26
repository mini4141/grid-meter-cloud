export interface Session {
  tenant: string;
  user: string;
  token: string;
}

const KEY = 'gridmeter.session';

export const TENANTS = [
  { id: 'aurora-power', name: 'Aurora Power' },
  { id: 'kita-denki', name: 'Kita Denki' },
];

export function loadSession(): Session | null {
  const raw = sessionStorage.getItem(KEY);
  return raw ? (JSON.parse(raw) as Session) : null;
}

export function saveSession(s: Session | null) {
  if (s) sessionStorage.setItem(KEY, JSON.stringify(s));
  else sessionStorage.removeItem(KEY);
}
