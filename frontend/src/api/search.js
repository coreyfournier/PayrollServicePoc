const ES_BASE = '/es/employee-search';

export async function fetchMapping() {
  const response = await fetch(`${ES_BASE}/_mapping`);
  if (!response.ok) throw new Error(`Failed to fetch mapping: ${response.status}`);
  const data = await response.json();
  const indexName = Object.keys(data)[0];
  return data[indexName]?.mappings?.properties || {};
}

function snakeToCamel(obj) {
  if (Array.isArray(obj)) return obj.map(snakeToCamel);
  if (obj === null || typeof obj !== 'object') return obj;

  const result = {};
  for (const [key, value] of Object.entries(obj)) {
    const camelKey = key.replace(/_([a-z])/g, (_, c) => c.toUpperCase());
    result[camelKey] = snakeToCamel(value);
  }
  return result;
}

export async function searchEmployees(esQuery) {
  const response = await fetch(`${ES_BASE}/_search`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ size: 200, ...esQuery }),
  });

  if (!response.ok) {
    throw new Error(`ES search failed: ${response.status}`);
  }

  const data = await response.json();
  const hits = data.hits?.hits || [];

  const employees = hits.map((hit) => {
    const source = snakeToCamel(hit._source);
    return {
      id: source.employeeId,
      firstName: source.firstName,
      lastName: source.lastName,
      email: source.email,
      payType: parseInt(source.payType, 10),
      payRate: source.payRate,
      payPeriodHours: source.payPeriodHours,
      isActive: source.isActive,
      hireDate: source.hireDate,
      payPeriods: source.payPeriods || [],
    };
  });

  return { employees, total: data.hits?.total?.value ?? employees.length };
}
