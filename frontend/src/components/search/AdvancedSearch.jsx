import QueryGroup from './QueryGroup';
import { createEmptyGroup } from '../../utils/searchQueryBuilder';

function AdvancedSearch({ group, onChange, onSearch, onReset, fieldDefs }) {
  const handleReset = () => {
    onChange(createEmptyGroup());
    onReset();
  };

  return (
    <div className="advanced-search">
      <QueryGroup group={group} onChange={onChange} depth={0} fieldDefs={fieldDefs} />
      <div className="advanced-search-actions">
        <button className="btn btn-primary btn-sm" onClick={onSearch}>
          Search
        </button>
        <button className="btn btn-secondary btn-sm" onClick={handleReset}>
          Reset
        </button>
      </div>
    </div>
  );
}

export default AdvancedSearch;
