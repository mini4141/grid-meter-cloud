import { AnonymousAuthenticationProvider } from '@microsoft/kiota-abstractions';
import { FetchRequestAdapter, KiotaClientFactory } from '@microsoft/kiota-http-fetchlibrary';
import { loadSession } from '../session';
import { createApiClient, type ApiClient } from './generated/apiClient';

/** Kiota client whose fetch adds the tenant header and bearer token of the current session. */
export function createClient(tenantOverride?: string): ApiClient {
  const authedFetch: typeof fetch = (input, init) => {
    const session = loadSession();
    const headers = new Headers(init?.headers);
    const tenant = tenantOverride ?? session?.tenant;
    if (tenant) headers.set('X-Tenant', tenant);
    if (session) headers.set('Authorization', `Bearer ${session.token}`);
    return fetch(input, { ...init, headers });
  };
  const adapter = new FetchRequestAdapter(
    new AnonymousAuthenticationProvider(),
    undefined,
    undefined,
    KiotaClientFactory.create(authedFetch),
  );
  // config.js (written at deploy time) points the SPA at the API when it is not served from the same origin.
  adapter.baseUrl = (window as { __GRIDMETER__?: { apiUrl?: string } }).__GRIDMETER__?.apiUrl || window.location.origin;
  return createApiClient(adapter);
}

export type { ApiClient };
