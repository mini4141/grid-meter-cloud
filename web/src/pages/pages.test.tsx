import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { ApiClient } from '../api/client';
import { AdminPage } from './AdminPage';
import { LoginPage } from './LoginPage';
import { SitePage } from './SitePage';
import { SitesPage } from './SitesPage';

vi.mock('@carbon/charts-react', () => ({
  LineChart: () => <div data-testid="line-chart" />,
  SimpleBarChart: () => <div data-testid="bar-chart" />,
}));
vi.mock('@carbon/charts-react/styles.css', () => ({}));

// Minimal stand-in for the Kiota client: only the calls the pages make.
function fakeApi(overrides: Record<string, unknown> = {}): ApiClient {
  const site = {
    get: async () => ({ id: 's1', name: 'Melbourne Depot', region: 'VIC', meters: [{ id: 'm1', externalId: 'MPAN-0001' }] }),
    consumption: {
      get: async () => ({ daily: [{ day: new Date('2026-11-01'), kwh: 23.2 }], perMeter: [{ meterId: 'MPAN-0001', kwh: 23.2 }], totalKwh: 23.2, peakKw: 1.9 }),
    },
    demandResponse: { get: async () => [], post: async () => ({}) },
    estimate: { get: async () => ({ pricePerKwh: 0.2, estimatedCost: 4.64, currency: 'AUD', totalKwh: 23.2 }) },
    meters: { post: async () => ({}) },
  };
  return {
    api: {
      auth: { token: { post: async () => ({ token: 't' }) } },
      sites: {
        get: async () => [{ id: 's1', name: 'Melbourne Depot', region: 'VIC', meterCount: 2 }],
        post: async () => ({}),
        byId: () => site,
      },
      admin: {
        info: { get: async () => ({ version: 'abc123', environment: 'Test', tariffEstimates: true, canToggleFlags: true }) },
        flags: { tariffEstimates: { put: async () => undefined } },
      },
      uploads: { post: async () => ({}) },
      ...overrides,
    },
  } as unknown as ApiClient;
}

describe('pages', () => {
  it('SitesPage renders the sites in a Carbon DataTable', async () => {
    render(
      <MemoryRouter>
        <SitesPage api={fakeApi()} />
      </MemoryRouter>,
    );
    expect(await screen.findByText('Melbourne Depot')).toBeInTheDocument();
    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  it('SitePage shows the consumption chart and the estimate when the flag is on', async () => {
    render(
      <MemoryRouter initialEntries={['/sites/s1']}>
        <Routes>
          <Route path="/sites/:id" element={<SitePage api={fakeApi()} />} />
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByTestId('line-chart')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('estimate-cost')).toHaveTextContent('AUD 4.64'));
  });

  it('AdminPage shows build info and the flag state', async () => {
    render(<AdminPage api={fakeApi()} />);
    expect(await screen.findByTestId('build-version')).toHaveTextContent('abc123');
  });

  it('LoginPage lists the tenants', () => {
    render(<LoginPage api={fakeApi()} onSignIn={() => undefined} />);
    expect(screen.getByRole('option', { name: 'Kita Denki' })).toBeInTheDocument();
  });
});
