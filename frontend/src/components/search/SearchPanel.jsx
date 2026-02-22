import { useState, useEffect, useCallback } from 'react';
import SimpleSearch from './SimpleSearch';
import AdvancedSearch from './AdvancedSearch';
import { buildFieldDefinitions, buildSimpleQuery, buildAdvancedQuery, createEmptyGroup } from '../../utils/searchQueryBuilder';
import { searchEmployees, fetchMapping } from '../../api/search';
import './SearchPanel.css';

function SearchPanel({ onSearch, onReset }) {
  const [mode, setMode] = useState('simple');
  const [advancedGroup, setAdvancedGroup] = useState(createEmptyGroup);
  const [resultCount, setResultCount] = useState(null);
  const [isSearching, setIsSearching] = useState(false);
  const [fieldDefs, setFieldDefs] = useState([]);

  useEffect(() => {
    fetchMapping()
      .then((mapping) => setFieldDefs(buildFieldDefinitions(mapping)))
      .catch((err) => console.error('Failed to load ES mapping:', err));
  }, []);

  const executeSearch = useCallback(async (esQuery) => {
    setIsSearching(true);
    try {
      const { employees, total } = await searchEmployees(esQuery);
      setResultCount(total);
      onSearch(employees);
    } catch (err) {
      console.error('Search failed:', err);
      setResultCount(null);
      onReset();
    } finally {
      setIsSearching(false);
    }
  }, [onSearch, onReset]);

  const handleSimpleQueryChange = useCallback((searchText, filters) => {
    const hasFilters = filters.status !== 'all' || filters.payType !== 'all';
    if (!searchText.trim() && !hasFilters) {
      setResultCount(null);
      onReset();
      return;
    }
    const query = buildSimpleQuery(searchText, filters, fieldDefs);
    executeSearch(query);
  }, [executeSearch, onReset, fieldDefs]);

  const handleAdvancedSearch = useCallback(() => {
    const query = buildAdvancedQuery(advancedGroup, fieldDefs);
    if (!query) {
      setResultCount(null);
      onReset();
      return;
    }
    executeSearch({ query });
  }, [advancedGroup, fieldDefs, executeSearch, onReset]);

  const handleAdvancedReset = useCallback(() => {
    setResultCount(null);
    onReset();
  }, [onReset]);

  const handleModeToggle = () => {
    setResultCount(null);
    onReset();
    setMode((prev) => (prev === 'simple' ? 'advanced' : 'simple'));
  };

  return (
    <div className="search-panel">
      <div className="search-panel-header">
        <h2 className="card-title">Employee Directory</h2>
        <button className="search-mode-toggle" onClick={handleModeToggle}>
          {mode === 'simple' ? 'Advanced Search' : 'Simple Search'}
        </button>
      </div>

      {mode === 'simple' ? (
        <SimpleSearch onQueryChange={handleSimpleQueryChange} />
      ) : (
        <AdvancedSearch
          group={advancedGroup}
          onChange={setAdvancedGroup}
          onSearch={handleAdvancedSearch}
          onReset={handleAdvancedReset}
          fieldDefs={fieldDefs}
        />
      )}

      {resultCount !== null && (
        <div className="search-result-count">
          {isSearching ? 'Searching...' : `${resultCount} result${resultCount !== 1 ? 's' : ''} found`}
        </div>
      )}
    </div>
  );
}

export default SearchPanel;
