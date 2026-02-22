// Fields to exclude from search UI
const EXCLUDED_FIELDS = new Set(['employee_id']);

// Enrichments for known fields that need special UI treatment.
// Everything else is auto-discovered from the ES mapping.
const FIELD_ENRICHMENTS = {
  'pay_type': { label: 'Pay Type', type: 'select', options: [{ value: '1', label: 'Hourly' }, { value: '2', label: 'Salary' }] },
  'is_active': { label: 'Status', type: 'select', options: [{ value: 'true', label: 'Active' }, { value: 'false', label: 'Inactive' }] },
};

// Map ES mapping types to UI field types
const ES_TYPE_MAP = {
  text: 'text',
  keyword: 'text',
  float: 'number',
  double: 'number',
  integer: 'number',
  long: 'number',
  half_float: 'number',
  scaled_float: 'number',
  date: 'date',
  boolean: 'select',
};

function snakeToLabel(str) {
  return str.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

/**
 * Builds field definitions dynamically from an ES index mapping.
 * @param {object} mappingProperties - The `properties` object from ES _mapping response
 * @returns {Array} Field definitions array for the search UI
 */
export function buildFieldDefinitions(mappingProperties) {
  const fields = [];

  function walk(properties, prefix = '', nested = false, category = 'Employee Info') {
    for (const [name, mapping] of Object.entries(properties)) {
      const fullField = prefix ? `${prefix}.${name}` : name;

      if (EXCLUDED_FIELDS.has(fullField)) continue;

      if (mapping.type === 'nested' && mapping.properties) {
        walk(mapping.properties, fullField, true, snakeToLabel(name));
        continue;
      }

      // If it's an object with properties but not nested, recurse
      if (!mapping.type && mapping.properties) {
        walk(mapping.properties, fullField, nested, category);
        continue;
      }

      const enrichment = FIELD_ENRICHMENTS[fullField] || {};
      const esType = mapping.type;
      const uiType = enrichment.type || ES_TYPE_MAP[esType];
      if (!uiType) continue;

      const fieldDef = {
        field: fullField,
        label: enrichment.label || snakeToLabel(name),
        type: uiType,
        esType,
        esField: fullField,
        nested,
        category: nested ? category : 'Employee Info',
      };

      if (enrichment.options) {
        fieldDef.options = enrichment.options;
      } else if (uiType === 'select' && esType === 'boolean') {
        fieldDef.options = [{ value: 'true', label: 'True' }, { value: 'false', label: 'False' }];
      }

      fields.push(fieldDef);
    }
  }

  walk(mappingProperties);
  return fields;
}

export const OPERATORS = {
  text: [
    { value: 'contains', label: 'contains' },
    { value: 'equals', label: 'equals' },
    { value: 'starts_with', label: 'starts with' },
  ],
  number: [
    { value: 'eq', label: '=' },
    { value: 'neq', label: '!=' },
    { value: 'gt', label: '>' },
    { value: 'gte', label: '>=' },
    { value: 'lt', label: '<' },
    { value: 'lte', label: '<=' },
  ],
  select: [
    { value: 'is', label: 'is' },
    { value: 'is_not', label: 'is not' },
  ],
  date: [
    { value: 'eq', label: '=' },
    { value: 'before', label: 'before' },
    { value: 'after', label: 'after' },
    { value: 'between', label: 'between' },
  ],
};

export function getFieldDef(fieldDefs, field) {
  return fieldDefs.find((f) => f.field === field);
}

export function buildSimpleQuery(searchText, filters = {}, fieldDefs = []) {
  const must = [];
  const filter = [];

  if (searchText && searchText.trim()) {
    // phrase_prefix only works on analyzed text fields, not keyword
    const analyzedFields = fieldDefs
      .filter((f) => f.type === 'text' && f.esType === 'text' && !f.nested)
      .map((f) => f.esField);
    const keywordFields = fieldDefs
      .filter((f) => f.type === 'text' && f.esType === 'keyword' && !f.nested)
      .map((f) => f.esField);

    const should = [];
    if (analyzedFields.length > 0) {
      should.push({
        multi_match: {
          query: searchText.trim(),
          fields: analyzedFields,
          type: 'phrase_prefix',
        },
      });
    }
    for (const kf of keywordFields) {
      should.push({ wildcard: { [kf]: { value: `*${searchText.trim().toLowerCase()}*` } } });
    }
    if (should.length > 0) {
      must.push({ bool: { should, minimum_should_match: 1 } });
    }
  }

  if (filters.status && filters.status !== 'all') {
    filter.push({ term: { is_active: filters.status === 'active' } });
  }

  if (filters.payType && filters.payType !== 'all') {
    filter.push({ term: { pay_type: filters.payType } });
  }

  if (must.length === 0 && filter.length === 0) {
    return { query: { match_all: {} } };
  }

  return {
    query: {
      bool: {
        ...(must.length > 0 ? { must } : {}),
        ...(filter.length > 0 ? { filter } : {}),
      },
    },
  };
}

export function buildAdvancedQuery(group, fieldDefs) {
  const clauses = [];

  for (const child of group.children) {
    if (child.type === 'group') {
      const subQuery = buildAdvancedQuery(child, fieldDefs);
      if (subQuery) clauses.push(subQuery);
    } else if (child.type === 'condition') {
      const condQuery = buildConditionQuery(child, fieldDefs);
      if (condQuery) clauses.push(condQuery);
    }
  }

  if (clauses.length === 0) return null;
  if (clauses.length === 1) return clauses[0];

  if (group.operator === 'AND') {
    return { bool: { must: clauses } };
  } else {
    return { bool: { should: clauses, minimum_should_match: 1 } };
  }
}

function buildConditionQuery(condition, fieldDefs) {
  const { field, operator, value, value2 } = condition;
  if (!field || !operator || (value === '' && value !== false && operator !== 'between')) return null;

  const fieldDef = getFieldDef(fieldDefs, field);
  if (!fieldDef) return null;

  const clause = buildClause(fieldDef, operator, value, value2);
  if (!clause) return null;

  if (fieldDef.nested) {
    const nestedPath = fieldDef.esField.substring(0, fieldDef.esField.indexOf('.'));
    return {
      nested: {
        path: nestedPath,
        query: clause,
      },
    };
  }

  return clause;
}

function buildClause(fieldDef, operator, value, value2) {
  const esField = fieldDef.esField;

  switch (fieldDef.type) {
    case 'text':
      switch (operator) {
        case 'contains':
          return { wildcard: { [esField]: { value: `*${value.toLowerCase()}*` } } };
        case 'equals': {
          // keyword fields don't have a .keyword subfield
          const termField = fieldDef.esType === 'keyword' ? esField : `${esField}.keyword`;
          return { term: { [termField]: value } };
        }
        case 'starts_with':
          return { prefix: { [esField]: value.toLowerCase() } };
        default:
          return null;
      }

    case 'number': {
      const numVal = parseFloat(value);
      if (isNaN(numVal)) return null;
      switch (operator) {
        case 'eq': return { term: { [esField]: numVal } };
        case 'neq': return { bool: { must_not: [{ term: { [esField]: numVal } }] } };
        case 'gt': return { range: { [esField]: { gt: numVal } } };
        case 'gte': return { range: { [esField]: { gte: numVal } } };
        case 'lt': return { range: { [esField]: { lt: numVal } } };
        case 'lte': return { range: { [esField]: { lte: numVal } } };
        default: return null;
      }
    }

    case 'select':
      switch (operator) {
        case 'is': {
          const parsed = value === 'true' ? true : value === 'false' ? false : value;
          return { term: { [esField]: parsed } };
        }
        case 'is_not': {
          const parsed = value === 'true' ? true : value === 'false' ? false : value;
          return { bool: { must_not: [{ term: { [esField]: parsed } }] } };
        }
        default:
          return null;
      }

    case 'date':
      switch (operator) {
        case 'eq': return { range: { [esField]: { gte: value, lte: value } } };
        case 'before': return { range: { [esField]: { lt: value } } };
        case 'after': return { range: { [esField]: { gt: value } } };
        case 'between':
          if (!value2) return null;
          return { range: { [esField]: { gte: value, lte: value2 } } };
        default:
          return null;
      }

    default:
      return null;
  }
}

export function createEmptyGroup(operator = 'AND') {
  return {
    type: 'group',
    operator,
    children: [createEmptyCondition()],
  };
}

export function createEmptyCondition() {
  return {
    type: 'condition',
    field: '',
    operator: '',
    value: '',
    value2: '',
  };
}
