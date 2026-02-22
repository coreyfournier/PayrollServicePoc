import QueryCondition from './QueryCondition';
import { createEmptyCondition, createEmptyGroup } from '../../utils/searchQueryBuilder';

const MAX_DEPTH = 3;

function QueryGroup({ group, onChange, onRemove, depth = 0, fieldDefs }) {
  const depthClass = depth > 0 ? `depth-${Math.min(depth, 2)}` : '';

  const handleOperatorToggle = (op) => {
    onChange({ ...group, operator: op });
  };

  const handleChildChange = (index, updatedChild) => {
    const newChildren = [...group.children];
    newChildren[index] = updatedChild;
    onChange({ ...group, children: newChildren });
  };

  const handleChildRemove = (index) => {
    const newChildren = group.children.filter((_, i) => i !== index);
    if (newChildren.length === 0) {
      newChildren.push(createEmptyCondition());
    }
    onChange({ ...group, children: newChildren });
  };

  const handleAddCondition = () => {
    onChange({ ...group, children: [...group.children, createEmptyCondition()] });
  };

  const handleAddGroup = () => {
    if (depth >= MAX_DEPTH - 1) return;
    onChange({ ...group, children: [...group.children, createEmptyGroup()] });
  };

  return (
    <div className={`query-group ${depthClass}`}>
      <div className="query-group-header">
        <div className="group-toggle">
          <button
            className={group.operator === 'AND' ? 'active' : ''}
            onClick={() => handleOperatorToggle('AND')}
          >
            AND
          </button>
          <button
            className={group.operator === 'OR' ? 'active' : ''}
            onClick={() => handleOperatorToggle('OR')}
          >
            OR
          </button>
        </div>
        <div className="query-group-actions">
          <button onClick={handleAddCondition}>+ Condition</button>
          {depth < MAX_DEPTH - 1 && (
            <button onClick={handleAddGroup}>+ Group</button>
          )}
          {onRemove && (
            <button className="remove-group" onClick={onRemove}>Remove</button>
          )}
        </div>
      </div>
      <div className="query-group-children">
        {group.children.map((child, index) =>
          child.type === 'group' ? (
            <QueryGroup
              key={index}
              group={child}
              onChange={(updated) => handleChildChange(index, updated)}
              onRemove={() => handleChildRemove(index)}
              depth={depth + 1}
              fieldDefs={fieldDefs}
            />
          ) : (
            <QueryCondition
              key={index}
              condition={child}
              onChange={(updated) => handleChildChange(index, updated)}
              onRemove={() => handleChildRemove(index)}
              fieldDefs={fieldDefs}
            />
          )
        )}
      </div>
    </div>
  );
}

export default QueryGroup;
