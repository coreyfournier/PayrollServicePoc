import { useState, useEffect, useRef } from 'react';
import { Search } from 'lucide-react';

function SimpleSearch({ onQueryChange }) {
  const [searchText, setSearchText] = useState('');
  const [filters, setFilters] = useState({ status: 'all', payType: 'all' });
  const debounceRef = useRef(null);

  useEffect(() => {
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      onQueryChange(searchText, filters);
    }, 300);
    return () => clearTimeout(debounceRef.current);
  }, [searchText, filters]);

  const handleFilterChange = (key, value) => {
    setFilters((prev) => ({ ...prev, [key]: value }));
  };

  return (
    <div className="simple-search">
      <div className="simple-search-input-row">
        <Search className="search-icon" />
        <input
          type="text"
          className="search-input"
          placeholder="Search by name or email..."
          value={searchText}
          onChange={(e) => setSearchText(e.target.value)}
        />
      </div>
      <div className="filter-chips">
        <div className="filter-chip-group">
          <span className="filter-chip-label">Status:</span>
          {['all', 'active', 'inactive'].map((val) => (
            <button
              key={val}
              className={`filter-chip ${filters.status === val ? 'active' : ''}`}
              onClick={() => handleFilterChange('status', val)}
            >
              {val.charAt(0).toUpperCase() + val.slice(1)}
            </button>
          ))}
        </div>
        <div className="filter-chip-divider" />
        <div className="filter-chip-group">
          <span className="filter-chip-label">Pay Type:</span>
          {[{ val: 'all', label: 'All' }, { val: '1', label: 'Hourly' }, { val: '2', label: 'Salary' }].map(({ val, label }) => (
            <button
              key={val}
              className={`filter-chip ${filters.payType === val ? 'active' : ''}`}
              onClick={() => handleFilterChange('payType', val)}
            >
              {label}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

export default SimpleSearch;
