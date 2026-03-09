import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fetchMapping, searchEmployees } from '../api/search';

// ---------------------------------------------------------------------------
// Mock global fetch
// ---------------------------------------------------------------------------
beforeEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// fetchMapping
// ---------------------------------------------------------------------------
describe('fetchMapping', () => {
  it('extracts properties from the ES mapping response', async () => {
    const mappingResponse = {
      'employee-search': {
        mappings: {
          properties: {
            first_name: { type: 'text' },
            pay_rate: { type: 'float' },
          },
        },
      },
    };

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(mappingResponse),
    }));

    const props = await fetchMapping();
    expect(props).toEqual({
      first_name: { type: 'text' },
      pay_rate: { type: 'float' },
    });
    expect(fetch).toHaveBeenCalledWith('/es/employee-search/_mapping');
  });

  it('returns empty object when mappings.properties is missing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ 'employee-search': { mappings: {} } }),
    }));

    const props = await fetchMapping();
    expect(props).toEqual({});
  });

  it('throws on non-ok response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false,
      status: 500,
    }));

    await expect(fetchMapping()).rejects.toThrow('Failed to fetch mapping: 500');
  });
});

// ---------------------------------------------------------------------------
// searchEmployees
// ---------------------------------------------------------------------------
describe('searchEmployees', () => {
  it('transforms ES hits into employee objects with camelCase keys', async () => {
    const esResponse = {
      hits: {
        total: { value: 1 },
        hits: [
          {
            _source: {
              employee_id: 'abc-123',
              first_name: 'John',
              last_name: 'Smith',
              email: 'john@example.com',
              pay_type: '1',
              pay_rate: 28.5,
              pay_period_hours: 40.0,
              is_active: true,
              hire_date: '2024-01-15',
              pay_periods: [
                {
                  pay_period_number: 55,
                  gross_pay: 1140.0,
                  net_pay: 950.0,
                },
              ],
            },
          },
        ],
      },
    };

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve(esResponse),
    }));

    const result = await searchEmployees({ query: { match_all: {} } });

    expect(result.total).toBe(1);
    expect(result.employees).toHaveLength(1);

    const emp = result.employees[0];
    expect(emp.id).toBe('abc-123');
    expect(emp.firstName).toBe('John');
    expect(emp.lastName).toBe('Smith');
    expect(emp.email).toBe('john@example.com');
    expect(emp.payType).toBe(1); // parsed int
    expect(emp.payRate).toBe(28.5);
    expect(emp.payPeriodHours).toBe(40.0);
    expect(emp.isActive).toBe(true);
    expect(emp.hireDate).toBe('2024-01-15');
    expect(emp.payPeriods).toHaveLength(1);
    expect(emp.payPeriods[0].payPeriodNumber).toBe(55);
    expect(emp.payPeriods[0].grossPay).toBe(1140.0);
    expect(emp.payPeriods[0].netPay).toBe(950.0);
  });

  it('handles empty results', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ hits: { total: { value: 0 }, hits: [] } }),
    }));

    const result = await searchEmployees({ query: { match_all: {} } });
    expect(result.employees).toEqual([]);
    expect(result.total).toBe(0);
  });

  it('defaults payPeriods to empty array when missing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({
        hits: {
          total: { value: 1 },
          hits: [{ _source: { employee_id: 'x', first_name: 'A', last_name: 'B', email: 'a@b.com', pay_type: '2', pay_rate: 50000, is_active: true, hire_date: '2024-01-01' } }],
        },
      }),
    }));

    const result = await searchEmployees({ query: { match_all: {} } });
    expect(result.employees[0].payPeriods).toEqual([]);
  });

  it('sends the query with size 200 and correct headers', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ hits: { total: { value: 0 }, hits: [] } }),
    }));

    const esQuery = { query: { match_all: {} } };
    await searchEmployees(esQuery);

    expect(fetch).toHaveBeenCalledWith('/es/employee-search/_search', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ size: 200, query: { match_all: {} } }),
    });
  });

  it('throws on non-ok response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: false,
      status: 400,
    }));

    await expect(searchEmployees({ query: {} })).rejects.toThrow('ES search failed: 400');
  });

  it('falls back to employees.length when total.value is missing', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({
        hits: {
          hits: [{ _source: { employee_id: 'x', first_name: 'A', last_name: 'B', email: 'a@b.com', pay_type: '1', pay_rate: 20, is_active: true, hire_date: '2024-01-01' } }],
        },
      }),
    }));

    const result = await searchEmployees({ query: { match_all: {} } });
    expect(result.total).toBe(1);
  });
});
