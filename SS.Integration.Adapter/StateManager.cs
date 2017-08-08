﻿//Copyright 2014 Spin Services Limited

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//    http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using log4net;
using SS.Integration.Adapter.Interface;
using SS.Integration.Adapter.MarketRules;
using SS.Integration.Adapter.MarketRules.Interfaces;
using SS.Integration.Adapter.Model;
using SS.Integration.Adapter.Model.Interfaces;
using SS.Integration.Adapter.Model.ProcessState;

namespace SS.Integration.Adapter
{
    public class StateManager : IStoredObjectProvider, IStateManager, IStateProvider
    {
        #region Constants

        private const string PLUGIN_STORE_PREFIX = "plugin_";

        #endregion

        #region Fields

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StateManager));

        private readonly IObjectProvider<IUpdatableMarketStateCollection> _persistanceLayer;
        private readonly IObjectProvider<IPluginFixtureState> _pluginPersistanceLayer;
        private readonly ConcurrentDictionary<string, MarketRulesManager> _rulesManagers;
        private readonly HashSet<IMarketRule> _rules;

        #endregion

        #region Constructors

        public StateManager(ISettings settings, IAdapterPlugin plugin)
        {
            if (settings == null)
                throw new ArgumentNullException("settings", "ISettings cannot be null");

            _persistanceLayer = new CachedObjectStoreWithPersistance<IUpdatableMarketStateCollection>(
                new BinaryStoreProvider<IUpdatableMarketStateCollection>(settings.MarketFiltersDirectory, "FilteredMarkets-{0}.bin"),
                "MarketFilters", settings.CacheExpiryInMins * 60);

            _pluginPersistanceLayer = new CachedObjectStoreWithPersistance<IPluginFixtureState>(
                new BinaryStoreProvider<IPluginFixtureState>(settings.MarketFiltersDirectory, "PluginStore-{0}.bin"),
                "MarketFilters", settings.CacheExpiryInMins * 60);

            _rulesManagers = new ConcurrentDictionary<string, MarketRulesManager>();

            SuspensionManager = new SuspensionManager(this, plugin);

            _rules = new HashSet<IMarketRule>
            {
                VoidUnSettledMarket.Instance,
                DeletedMarketsRule.Instance,
                InactiveMarketsFilteringRule.Instance
            };

            if (settings.DeltaRuleEnabled)
            {
                _rules.Add(DeltaRule.Instance);
            }

            foreach (var rule in _rules)
            {
                Logger.DebugFormat("Rule {0} correctly loaded", rule.Name);
            }
        }

        #endregion

        #region Internal methods

        internal void OverwriteRuleList(IEnumerable<IMarketRule> rules)
        {
            if (rules != null)
            {
                _rules.Clear();
                _rules.UnionWith(rules);

                foreach (var rule in _rules)
                {
                    Logger.DebugFormat("Rule {0} correctly loaded", rule.Name);
                }
            }
        }

        internal void AddRules(IEnumerable<IMarketRule> rules)
        {
            if (rules == null)
                return;

            foreach (var rule in rules)
            {
                Logger.Debug(_rules.Add(rule)
                    ? $"Rule {rule.Name} correctly loaded"
                    : $"Rule {rule.Name} already loaded");
            }
        }

        internal IEnumerable<IMarketRule> LoadedRules => _rules;

        #endregion

        #region Implementation of IStoredObjectProvider

        public IUpdatableMarketStateCollection GetObject(string fixtureId)
        {
            return _persistanceLayer.GetObject(fixtureId);
        }

        public void SetObject(string fixtureId, IUpdatableMarketStateCollection state)
        {
            _persistanceLayer.SetObject(fixtureId, state);
        }

        public void Remove(string fixtureId)
        {
            _persistanceLayer.Remove(fixtureId);
        }

        #endregion

        #region Implementation of IStateProvider

        IMarketStateCollection IStateProvider.GetMarketsState(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
                throw new ArgumentNullException("fixtureId", "fixtureId cannot be null");

            return _rulesManagers.ContainsKey(fixtureId) ? _rulesManagers[fixtureId].CurrentState : null;
        }

        public T GetPluginFixtureState<T>(string fixtureId) where T : IPluginFixtureState
        {
            var tmp = GetPluginFixtureState(fixtureId);
            if (tmp == null)
                return default(T);

            return (T)tmp;
        }

        public IPluginFixtureState GetPluginFixtureState(string fixtureId)
        {
            return _pluginPersistanceLayer.GetObject(PLUGIN_STORE_PREFIX + fixtureId);
        }

        public void AddOrUpdatePluginFixtureState(IPluginFixtureState state)
        {
            if (state == null)
                return;

            if (string.IsNullOrEmpty(state.FixtureId))
                throw new Exception("FixtureId cannot be null");

            _pluginPersistanceLayer.SetObject(PLUGIN_STORE_PREFIX + state.FixtureId, state);
        }

        public void RemovePluginState(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
                throw new Exception("FixtureId cannot be null");

            _pluginPersistanceLayer.Remove(PLUGIN_STORE_PREFIX + fixtureId);
        }

        public ISuspensionManager SuspensionManager { get; private set; }

        #endregion

        #region Implementation of IStateManager

        public IMarketRulesManager CreateNewMarketRuleManager(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
                throw new ArgumentNullException("fixtureId", "fixtureId cannot be null or empty");

            if (_rulesManagers.ContainsKey(fixtureId))
                return _rulesManagers[fixtureId];

            var rule_manager = new MarketRulesManager(fixtureId, this, _rules);
            _rulesManagers[fixtureId] = rule_manager;

            return rule_manager;
        }

        public void ClearState(string fixtureId)
        {
            if (string.IsNullOrEmpty(fixtureId))
                return;

            Logger.DebugFormat("Clearing data state for fixtureId={0}", fixtureId);

            if (_rulesManagers.ContainsKey(fixtureId))
            {
                MarketRulesManager dummy;
                _rulesManagers.TryRemove(fixtureId, out dummy);
            }

            _persistanceLayer.Remove(fixtureId);
        }

        public IStateProvider StateProvider { get { return this; } }

        #endregion
    }
}
