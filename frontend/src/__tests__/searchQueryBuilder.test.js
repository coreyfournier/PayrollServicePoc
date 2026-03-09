import { describe, it, expect } from 'vitest';
import {
  buildFieldDefinitions,
  OPERATORS,
  getFieldDef,
  buildSimpleQuery,
  buildAdvancedQuery,
  createEmptyGroup,
  createEmptyCondition,
} from '../utils/searchQueryBuilder';

// ---------------------------------------------------------------------------
// buildFieldDefinitions
// ---------------------------------------------------------------------------
describe('buildFieldDefinitions', () => {
  it('maps ES text type to UI text type', () => {
    const props = { first_name: { type: 'text' } };
    const defs = buildFieldDefinitions(props);
    expect(defs).toHaveLength(1);
    expect(defs[0]).toMatchObject({ field: 'first_name', type: 'text', esType: 'text', label: 'First Name' });
  });

  it('maps ES keyword type to UI text type', () => {
    const defs = buildFieldDefinitions({ email: { type: 'keyword' } });
    expect(defs[0]).toMatchObject({ type: 'text', esType: 'keyword' });
  });

  it('maps numeric ES types to UI number type', () => {
    const numericTypes = ['float', 'double', 'integer', 'long', 'half_float', 'scaled_float'];
    for (const esType of numericTypes) {
      const defs = buildFieldDefinitions({ pay_rate: { type: esType } });
      expect(defs[0].type).toBe('number');
    }
  });

  it('maps ES date type to UI date type', () => {
    const defs = buildFieldDefinitions({ hire_date: { type: 'date' } });
    expect(defs[0]).toMatchObject({ type: 'date', esType: 'date' });
  });

  it('maps ES boolean type to UI select type with True/False options', () => {
    const defs = buildFieldDefinitions({ some_flag: { type: 'boolean' } });
    expect(defs[0]).toMatchObject({ type: 'select', esType: 'boolean' });
    expect(defs[0].options).toEqual([
      { value: 'true', label: 'True' },
      { value: 'false', label: 'False' },
    ]);
  });

  it('excludes employee_id field', () => {
    const props = {
      employee_id: { type: 'keyword' },
      first_name: { type: 'text' },
    };
    const defs = buildFieldDefinitions(props);
    expect(defs).toHaveLength(1);
    expect(defs[0].field).toBe('first_name');
  });

  it('handles nested fields with nested=true and correct category', () => {
    const props = {
      pay_periods: {
        type: 'nested',
        properties: {
          gross_pay: { type: 'float' },
          net_pay: { type: 'float' },
        },
      },
    };
    const defs = buildFieldDefinitions(props);
    expect(defs).toHaveLength(2);
    expect(defs[0]).toMatchObject({
      field: 'pay_periods.gross_pay',
      nested: true,
      category: 'Pay Periods',
    });
    expect(defs[1]).toMatchObject({
      field: 'pay_periods.net_pay',
      nested: true,
      category: 'Pay Periods',
    });
  });

  it('recurses into object properties (non-nested)', () => {
    const props = {
      address: {
        properties: {
          city: { type: 'text' },
        },
      },
    };
    const defs = buildFieldDefinitions(props);
    expect(defs).toHaveLength(1);
    expect(defs[0]).toMatchObject({ field: 'address.city', nested: false, category: 'Employee Info' });
  });

  it('applies enrichment for pay_type', () => {
    const defs = buildFieldDefinitions({ pay_type: { type: 'keyword' } });
    expect(defs[0]).toMatchObject({
      field: 'pay_type',
      label: 'Pay Type',
      type: 'select',
      options: [{ value: '1', label: 'Hourly' }, { value: '2', label: 'Salary' }],
    });
  });

  it('applies enrichment for is_active', () => {
    const defs = buildFieldDefinitions({ is_active: { type: 'boolean' } });
    expect(defs[0]).toMatchObject({
      field: 'is_active',
      label: 'Status',
      type: 'select',
      options: [{ value: 'true', label: 'Active' }, { value: 'false', label: 'Inactive' }],
    });
  });

  it('skips unknown ES types', () => {
    const defs = buildFieldDefinitions({ unknown_field: { type: 'geo_point' } });
    expect(defs).toHaveLength(0);
  });
});

// ---------------------------------------------------------------------------
// OPERATORS
// ---------------------------------------------------------------------------
describe('OPERATORS', () => {
  it('has text operators', () => {
    expect(OPERATORS.text.map((o) => o.value)).toEqual(['contains', 'equals', 'starts_with']);
  });

  it('has number operators', () => {
    expect(OPERATORS.number.map((o) => o.value)).toEqual(['eq', 'neq', 'gt', 'gte', 'lt', 'lte']);
  });

  it('has select operators', () => {
    expect(OPERATORS.select.map((o) => o.value)).toEqual(['is', 'is_not']);
  });

  it('has date operators', () => {
    expect(OPERATORS.date.map((o) => o.value)).toEqual(['eq', 'before', 'after', 'between']);
  });
});

// ---------------------------------------------------------------------------
// getFieldDef
// ---------------------------------------------------------------------------
describe('getFieldDef', () => {
  const fieldDefs = [
    { field: 'first_name', type: 'text' },
    { field: 'pay_rate', type: 'number' },
  ];

  it('finds a field by name', () => {
    expect(getFieldDef(fieldDefs, 'pay_rate')).toEqual({ field: 'pay_rate', type: 'number' });
  });

  it('returns undefined for a missing field', () => {
    expect(getFieldDef(fieldDefs, 'nonexistent')).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// createEmptyGroup / createEmptyCondition
// ---------------------------------------------------------------------------
describe('createEmptyGroup', () => {
  it('creates an AND group by default with one empty condition', () => {
    const group = createEmptyGroup();
    expect(group.type).toBe('group');
    expect(group.operator).toBe('AND');
    expect(group.children).toHaveLength(1);
    expect(group.children[0].type).toBe('condition');
  });

  it('creates an OR group when specified', () => {
    const group = createEmptyGroup('OR');
    expect(group.operator).toBe('OR');
  });
});

describe('createEmptyCondition', () => {
  it('creates a condition with empty defaults', () => {
    const cond = createEmptyCondition();
    expect(cond).toEqual({ type: 'condition', field: '', operator: '', value: '', value2: '' });
  });
});

// ---------------------------------------------------------------------------
// buildSimpleQuery
// ---------------------------------------------------------------------------
describe('buildSimpleQuery', () => {
  const fieldDefs = buildFieldDefinitions({
    first_name: { type: 'text' },
    last_name: { type: 'text' },
    email: { type: 'keyword' },
    is_active: { type: 'boolean' },
    pay_type: { type: 'keyword' },
  });

  it('returns match_all when no text or filters', () => {
    expect(buildSimpleQuery('', {}, fieldDefs)).toEqual({ query: { match_all: {} } });
  });

  it('returns match_all for whitespace-only text', () => {
    expect(buildSimpleQuery('   ', {}, fieldDefs)).toEqual({ query: { match_all: {} } });
  });

  it('builds phrase_prefix for text search on analyzed fields and wildcard for keyword fields', () => {
    const q = buildSimpleQuery('john', {}, fieldDefs);
    const boolShould = q.query.bool.must[0].bool.should;
    // Should have multi_match for text fields
    const multiMatch = boolShould.find((c) => c.multi_match);
    expect(multiMatch).toBeDefined();
    expect(multiMatch.multi_match.query).toBe('john');
    expect(multiMatch.multi_match.type).toBe('phrase_prefix');
    // Should have wildcard for keyword field (email)
    const wildcard = boolShould.find((c) => c.wildcard);
    expect(wildcard).toBeDefined();
    expect(wildcard.wildcard.email.value).toBe('*john*');
  });

  it('adds status filter when not "all"', () => {
    const q = buildSimpleQuery('', { status: 'active' }, fieldDefs);
    expect(q.query.bool.filter).toEqual([{ term: { is_active: true } }]);
  });

  it('adds inactive status filter', () => {
    const q = buildSimpleQuery('', { status: 'inactive' }, fieldDefs);
    expect(q.query.bool.filter).toEqual([{ term: { is_active: false } }]);
  });

  it('ignores status=all', () => {
    expect(buildSimpleQuery('', { status: 'all' }, fieldDefs)).toEqual({ query: { match_all: {} } });
  });

  it('adds payType filter', () => {
    const q = buildSimpleQuery('', { payType: '1' }, fieldDefs);
    expect(q.query.bool.filter).toEqual([{ term: { pay_type: '1' } }]);
  });

  it('combines text search with filters', () => {
    const q = buildSimpleQuery('jane', { status: 'active', payType: '2' }, fieldDefs);
    expect(q.query.bool.must).toBeDefined();
    expect(q.query.bool.filter).toHaveLength(2);
  });
});

// ---------------------------------------------------------------------------
// buildAdvancedQuery
// ---------------------------------------------------------------------------
describe('buildAdvancedQuery', () => {
  const fieldDefs = buildFieldDefinitions({
    first_name: { type: 'text' },
    pay_rate: { type: 'float' },
    is_active: { type: 'boolean' },
    hire_date: { type: 'date' },
    email: { type: 'keyword' },
    pay_periods: {
      type: 'nested',
      properties: {
        gross_pay: { type: 'float' },
      },
    },
  });

  it('returns null for an empty group', () => {
    const group = { type: 'group', operator: 'AND', children: [] };
    expect(buildAdvancedQuery(group, fieldDefs)).toBeNull();
  });

  it('returns null when the single condition is incomplete', () => {
    const group = {
      type: 'group',
      operator: 'AND',
      children: [{ type: 'condition', field: '', operator: '', value: '', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toBeNull();
  });

  // --- text operators ---
  it('builds wildcard for text contains', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'first_name', operator: 'contains', value: 'john', value2: '' }],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q).toEqual({ wildcard: { first_name: { value: '*john*' } } });
  });

  it('builds term with .keyword for text equals on text field', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'first_name', operator: 'equals', value: 'John', value2: '' }],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q).toEqual({ term: { 'first_name.keyword': 'John' } });
  });

  it('builds term without .keyword for equals on keyword field', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'email', operator: 'equals', value: 'test@test.com', value2: '' }],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q).toEqual({ term: { email: 'test@test.com' } });
  });

  it('builds prefix for text starts_with', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'first_name', operator: 'starts_with', value: 'jo', value2: '' }],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q).toEqual({ prefix: { first_name: 'jo' } });
  });

  // --- number operators ---
  it('builds term for number eq', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'eq', value: '50', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ term: { pay_rate: 50 } });
  });

  it('builds must_not for number neq', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'neq', value: '50', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ bool: { must_not: [{ term: { pay_rate: 50 } }] } });
  });

  it('builds range for number gt', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'gt', value: '30', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { pay_rate: { gt: 30 } } });
  });

  it('builds range for number gte', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'gte', value: '30', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { pay_rate: { gte: 30 } } });
  });

  it('builds range for number lt', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'lt', value: '100', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { pay_rate: { lt: 100 } } });
  });

  it('builds range for number lte', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'lte', value: '100', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { pay_rate: { lte: 100 } } });
  });

  it('returns null for non-numeric number value', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'eq', value: 'abc', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toBeNull();
  });

  // --- select operators ---
  it('builds term for select is (boolean true)', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'is_active', operator: 'is', value: 'true', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ term: { is_active: true } });
  });

  it('builds term for select is (boolean false)', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'is_active', operator: 'is', value: 'false', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ term: { is_active: false } });
  });

  it('builds must_not for select is_not', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'is_active', operator: 'is_not', value: 'true', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ bool: { must_not: [{ term: { is_active: true } }] } });
  });

  // --- date operators ---
  it('builds range for date eq', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'hire_date', operator: 'eq', value: '2024-01-15', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { hire_date: { gte: '2024-01-15', lte: '2024-01-15' } } });
  });

  it('builds range for date before', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'hire_date', operator: 'before', value: '2024-06-01', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { hire_date: { lt: '2024-06-01' } } });
  });

  it('builds range for date after', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'hire_date', operator: 'after', value: '2024-06-01', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { hire_date: { gt: '2024-06-01' } } });
  });

  it('builds range for date between', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'hire_date', operator: 'between', value: '2024-01-01', value2: '2024-12-31' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toEqual({ range: { hire_date: { gte: '2024-01-01', lte: '2024-12-31' } } });
  });

  it('returns null for date between with missing value2', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'hire_date', operator: 'between', value: '2024-01-01', value2: '' }],
    };
    expect(buildAdvancedQuery(group, fieldDefs)).toBeNull();
  });

  // --- AND / OR groups ---
  it('combines two conditions with AND (must)', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [
        { type: 'condition', field: 'first_name', operator: 'contains', value: 'john', value2: '' },
        { type: 'condition', field: 'pay_rate', operator: 'gt', value: '20', value2: '' },
      ],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q.bool.must).toHaveLength(2);
  });

  it('combines two conditions with OR (should)', () => {
    const group = {
      type: 'group', operator: 'OR',
      children: [
        { type: 'condition', field: 'first_name', operator: 'contains', value: 'john', value2: '' },
        { type: 'condition', field: 'first_name', operator: 'contains', value: 'jane', value2: '' },
      ],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q.bool.should).toHaveLength(2);
    expect(q.bool.minimum_should_match).toBe(1);
  });

  it('handles nested groups', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [
        { type: 'condition', field: 'pay_rate', operator: 'gt', value: '20', value2: '' },
        {
          type: 'group', operator: 'OR',
          children: [
            { type: 'condition', field: 'first_name', operator: 'contains', value: 'john', value2: '' },
            { type: 'condition', field: 'first_name', operator: 'contains', value: 'jane', value2: '' },
          ],
        },
      ],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q.bool.must).toHaveLength(2);
    // Second child should be the OR sub-group
    const orGroup = q.bool.must[1];
    expect(orGroup.bool.should).toHaveLength(2);
  });

  // --- nested field wrapping ---
  it('wraps nested field conditions in a nested query', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_periods.gross_pay', operator: 'gt', value: '1000', value2: '' }],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    expect(q).toEqual({
      nested: {
        path: 'pay_periods',
        query: { range: { 'pay_periods.gross_pay': { gt: 1000 } } },
      },
    });
  });

  // --- single condition unwrapping ---
  it('returns the single clause directly without bool wrapper for single condition', () => {
    const group = {
      type: 'group', operator: 'AND',
      children: [{ type: 'condition', field: 'pay_rate', operator: 'eq', value: '50', value2: '' }],
    };
    const q = buildAdvancedQuery(group, fieldDefs);
    // Should be the clause directly, not wrapped in bool.must
    expect(q).toEqual({ term: { pay_rate: 50 } });
  });
});
