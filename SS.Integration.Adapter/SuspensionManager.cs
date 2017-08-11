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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using log4net;
using SS.Integration.Adapter.Exceptions;
using SS.Integration.Adapter.MarketRules.Interfaces;
using SS.Integration.Adapter.Model;
using SS.Integration.Adapter.Model.Enums;
using SS.Integration.Adapter.Model.Interfaces;

namespace SS.Integration.Adapter
{
    internal class SuspensionManager : ISuspensionManager
    {
        #region Fields

        private readonly ILog _logger = LogManager.GetLogger(typeof(SuspensionManager));
        private readonly Action<IMarketStateCollection> _default;
        private readonly Action<IMarketStateCollection> _disconnected;
        private readonly Action<IMarketStateCollection> _fixtureDeleted;
        private readonly Action<IMarketStateCollection> _disposing;
        private readonly Action<IMarketStateCollection> _error;
        private readonly IStateProvider _stateProvider;
        private readonly IAdapterPlugin _plugin;

        #endregion

        #region Constructors

        internal SuspensionManager(IStateProvider stateProvider, IAdapterPlugin plugin)
        {
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
            _plugin = plugin ?? throw new AggregateException(nameof(plugin));

            BuildDefaultStrategies();

            _disposing = SuspendFixtureStrategy;
            _error = SuspendFixtureStrategy;
            _disconnected = SuspendFixtureStrategy;
            _default = SuspendFixtureStrategy;
            _fixtureDeleted = SuspendFixtureAndSetMatchStatusDeleted;
        }

        #endregion

        #region Implementation of ISuspensionManager

        public Action<IMarketStateCollection> DoNothingStrategy { get; private set; }

        public Action<IMarketStateCollection> SuspendFixtureStrategy { get; private set; }

        public Action<IMarketStateCollection> SuspendFixtureIfInPlayStrategy { get; private set; }

        public Action<IMarketStateCollection> SuspendAllMarketsStrategy { get; private set; }

        public Action<IMarketStateCollection> SuspendInPlayMarketsStrategy { get; private set; }

        public Action<IMarketStateCollection> SuspendAllMarketsAndSetMatchStatusDeleted { get; private set; }

        public Action<IMarketStateCollection> SuspendFixtureAndSetMatchStatusDeleted { get; private set; }

        public void Suspend(string fixtureId, SuspensionReason reason = SuspensionReason.FixtureDisposing)
        {
            Action<IMarketStateCollection> action;
            switch (reason)
            {
                case SuspensionReason.FixtureDisposing:
                    action = _disposing;
                    break;
                case SuspensionReason.DisconnectEvent:
                    action = _disconnected;
                    break;
                case SuspensionReason.FixtureDeleted:
                    action = _fixtureDeleted;
                    break;
                case SuspensionReason.FixtureErrored:
                    action = _error;
                    break;
                default:
                    action = _default;
                    break;
            }

            IMarketStateCollection state = _stateProvider.GetMarketsState(fixtureId);
            if (state == null)
            {
                _logger.WarnFormat("State is not present for fixtureId={0} - can't suspend with reason={1}",
                    fixtureId, reason);
                return;
            }

            _logger.InfoFormat("Performing suspension for fixtureId={0} due reason={1}", fixtureId, reason);

            try
            {
                action(state);
            }
            catch (Exception e)
            {
                _logger.Error($"An error occured while performing suspend action on fixtureId={fixtureId}", e);
                throw new PluginException($"Plugin Suspend Fixture with fixtureId={fixtureId} error occured", e);
            }
        }

        public void Unsuspend(string fixtureId)
        {
            IMarketStateCollection state = _stateProvider.GetMarketsState(fixtureId);
            List<IMarketState> marketStates;

            if (state == null)
            {
                _logger.WarnFormat("State is not present for fixtureId={0} - can't unsuspend", fixtureId);
                return;
            }

            var fixture = GetUnsuspendedFixture(state, out marketStates);

            if (fixture.Markets.Any())
            {
                _logger.InfoFormat("Unsuspending previously suspended markets in {0}", fixture);
                try
                {
                    _plugin.ProcessStreamUpdate(fixture);
                }
                catch (Exception ex)
                {
                    throw new PluginException($"Plugin ProcessStreamUpdate Fixture with fixtureId={fixtureId} error occured", ex);
                }
            }

            ((IUpdatableMarketStateCollection)state).OnMarketsForcedUnsuspension(marketStates);
        }

        #endregion

        #region Private methods

        private void BuildDefaultStrategies()
        {
            DoNothingStrategy = x => { };

            SuspendFixtureStrategy = x => _plugin.Suspend(x.FixtureId);

            SuspendFixtureIfInPlayStrategy = x => { if (x.FixtureStatus == MatchStatus.InRunning) _plugin.Suspend(x.FixtureId); };

            SuspendAllMarketsStrategy = x =>
            {
                IEnumerable<IMarketState> includedMarketStates;
                var fixture = GetFixtureWithSuspendedMarkets(x, out includedMarketStates);

                _logger.DebugFormat("Sending suspension command through the plugin for fixtureId={0}", x.FixtureId);

                try
                {
                    _plugin.ProcessStreamUpdate(fixture);
                }
                catch (Exception ex)
                {
                    throw new PluginException($"Plugin ProcessStreamUpdate Fixture with fixtureId={x.FixtureId} error occured", ex);
                }

                _logger.InfoFormat("Marking markets for fixtureId={0} as suspended", x.FixtureId);
                ((IUpdatableMarketStateCollection)x).OnMarketsForcedSuspension(includedMarketStates);
            };

            SuspendAllMarketsAndSetMatchStatusDeleted = x =>
            {
                IEnumerable<IMarketState> includedMarketStates;

                var fixture = GetFixtureWithSuspendedMarkets(x, out includedMarketStates);
                fixture.MatchStatus = ((int)MatchStatus.Deleted).ToString();

                _logger.DebugFormat("Sending suspension command through the plugin for fixtureId={0} and setting its MatchStatus as deleted", x.FixtureId);
                try
                {
                    _plugin.ProcessStreamUpdate(fixture);
                }
                catch (Exception ex)
                {
                    throw new PluginException($"Plugin ProcessStreamUpdate Fixture with fixtureId={x.FixtureId} error occured", ex);
                }

                _logger.InfoFormat("Marking markets for fixtureId={0} as suspended", x.FixtureId);
                ((IUpdatableMarketStateCollection)x).OnMarketsForcedSuspension(includedMarketStates);
            };

            SuspendFixtureAndSetMatchStatusDeleted = x =>
            {
                var fixture = new Fixture
                {
                    Id = x.FixtureId,
                    MatchStatus = ((int)MatchStatus.Deleted).ToString(),
                    Sequence = x.FixtureSequence
                };


                _logger.DebugFormat("Sending suspension command through the plugin for fixtureId={0} and setting its MatchStatus as deleted", x.FixtureId);
                try
                {
                    _plugin.ProcessStreamUpdate(fixture);
                }
                catch (Exception ex)
                {
                    throw new PluginException($"Plugin ProcessStreamUpdate Fixture with fixtureId={x.FixtureId} error occured", ex);
                }

                _logger.InfoFormat("Marking markets for fixtureId={0} as suspended", x.FixtureId);
            };

            SuspendInPlayMarketsStrategy = x =>
            {
                List<IMarketState> includedMarketStates = new List<IMarketState>();

                var fixture = new Fixture
                {
                    Id = x.FixtureId,
                    MatchStatus = ((int)x.FixtureStatus).ToString(),
                    Sequence = x.FixtureSequence
                };

                foreach (var marketId in x.Markets)
                {

                    // we take a conservative approach here.
                    // If, for any reason, the traded_in_play
                    // is not present, we assume it is. Better
                    // to suspend more markets, than less
                    IMarketState state = x[marketId];
                    if (state.HasTag("traded_in_play") &&
                        string.Equals(state.GetTagValue("traded_in_play"), "false", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.DebugFormat("marketId={0} of fixtureId={1} will not be suspended as it is not traded in play", marketId, fixture.Id);
                        continue;
                    }

                    if (!state.HasBeenActive)
                    {
                        _logger.DebugFormat("marketId={0} of fixtureId={1} will not be suspended as it has not been active before", marketId, fixture.Id);
                        continue;
                    }

                    includedMarketStates.Add(state);
                    fixture.Markets.Add(CreateMarket(x[marketId]));
                }


                _logger.DebugFormat("Sending suspension command through the plugin for in-play markets of fixtureId={0}", x.FixtureId);
                try
                {
                    _plugin.ProcessStreamUpdate(fixture);
                }
                catch (Exception ex)
                {
                    throw new PluginException($"Plugin ProcessStreamUpdate Fixture with fixtureId={x.FixtureId} error occured", ex);
                }

                _logger.InfoFormat("Marking markets for fixtureId={0} as suspended", x.FixtureId);
                ((IUpdatableMarketStateCollection)x).OnMarketsForcedSuspension(includedMarketStates);
            };
        }

        private Fixture GetUnsuspendedFixture(IMarketStateCollection state, out List<IMarketState> marketStates)
        {
            marketStates = new List<IMarketState>();

            var fixture = new Fixture
            {
                Id = state.FixtureId,
                MatchStatus = ((int)state.FixtureStatus).ToString(),
                Sequence = state.FixtureSequence
            };

            foreach (var marketId in state.Markets)
            {
                var marketState = state[marketId];
                if (marketState == null)
                    continue;

                marketStates.Add(marketState);
                //only unsuspend market if it's suspended by Adapter and not suspended in the feed
                if (marketState.IsForcedSuspended && !marketState.IsSuspended)
                {
                    fixture.Markets.Add(CreateMarket(state[marketId], false));
                }
            }

            return fixture;
        }

        private static Fixture GetFixtureWithSuspendedMarkets(IMarketStateCollection state, out IEnumerable<IMarketState> includedMarketStates)
        {
            includedMarketStates = new List<IMarketState>();

            var fixture = new Fixture
            {
                Id = state.FixtureId,
                MatchStatus = ((int)state.FixtureStatus).ToString(),
                Sequence = state.FixtureSequence
            };

            foreach (var marketId in state.Markets)
            {
                ((List<IMarketState>)includedMarketStates).Add(state[marketId]);
                fixture.Markets.Add(CreateMarket(state[marketId]));
            }

            return fixture;
        }

        private static Market CreateMarket(IMarketState marketState, bool isSuspended = true)
        {
            var market = new Market(marketState.Id) { IsSuspended = isSuspended };
            foreach (var seln in marketState.Selections)
                market.Selections.Add(new Selection { Id = seln.Id, Status = SelectionStatus.Pending, Tradable = !isSuspended });

            return market;
        }

        #endregion
    }
}
