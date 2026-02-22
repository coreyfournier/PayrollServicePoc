import { useMemo } from 'react';
import { X } from 'lucide-react';
import { OPERATORS, getFieldDef } from '../../utils/searchQueryBuilder';

function QueryCondition({ condition, onChange, onRemove, fieldDefs }) {
  const categories = useMemo(
    () => [...new Set(fieldDefs.map((f) => f.category))],
    [fieldDefs]
  );

  const fieldDef = getFieldDef(fieldDefs, condition.field);
  const operators = fieldDef ? OPERATORS[fieldDef.type] || [] : [];

  const handleFieldChange = (e) => {
    const newField = e.target.value;
    const newFieldDef = getFieldDef(fieldDefs, newField);
    const newOperators = newFieldDef ? OPERATORS[newFieldDef.type] || [] : [];
    onChange({
      ...condition,
      field: newField,
      operator: newOperators.length > 0 ? newOperators[0].value : '',
      value: '',
      value2: '',
    });
  };

  const handleOperatorChange = (e) => {
    onChange({ ...condition, operator: e.target.value, value2: '' });
  };

  const handleValueChange = (e) => {
    onChange({ ...condition, value: e.target.value });
  };

  const handleValue2Change = (e) => {
    onChange({ ...condition, value2: e.target.value });
  };

  const renderValueInput = () => {
    if (!fieldDef) return <input className="condition-value" disabled placeholder="Select a field" />;

    if (fieldDef.type === 'select') {
      return (
        <select className="condition-value" value={condition.value} onChange={handleValueChange}>
          <option value="">Select...</option>
          {(fieldDef.options || []).map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
      );
    }

    if (fieldDef.type === 'date') {
      return (
        <>
          <input
            type="date"
            className="condition-value"
            value={condition.value}
            onChange={handleValueChange}
          />
          {condition.operator === 'between' && (
            <input
              type="date"
              className="condition-value2"
              value={condition.value2}
              onChange={handleValue2Change}
            />
          )}
        </>
      );
    }

    if (fieldDef.type === 'number') {
      return (
        <input
          type="number"
          step="any"
          className="condition-value"
          value={condition.value}
          onChange={handleValueChange}
          placeholder="Value"
        />
      );
    }

    return (
      <input
        type="text"
        className="condition-value"
        value={condition.value}
        onChange={handleValueChange}
        placeholder="Value"
      />
    );
  };

  return (
    <div className="query-condition">
      <select className="condition-field" value={condition.field} onChange={handleFieldChange}>
        <option value="">Select field...</option>
        {categories.map((cat) => (
          <optgroup key={cat} label={cat}>
            {fieldDefs.filter((f) => f.category === cat).map((f) => (
              <option key={f.field} value={f.field}>{f.label}</option>
            ))}
          </optgroup>
        ))}
      </select>

      <select
        className="condition-operator"
        value={condition.operator}
        onChange={handleOperatorChange}
        disabled={!condition.field}
      >
        <option value="">Operator...</option>
        {operators.map((op) => (
          <option key={op.value} value={op.value}>{op.label}</option>
        ))}
      </select>

      {renderValueInput()}

      <button className="condition-remove" onClick={onRemove} title="Remove condition">
        <X />
      </button>
    </div>
  );
}

export default QueryCondition;
