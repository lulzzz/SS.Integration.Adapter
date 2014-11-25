﻿using System;
using System.Collections.Generic;
using SS.Integration.Adapter.Interface;

namespace SS.Integration.Adapter.Diagnostics.Model.Interface
{
    public interface ISupervisor : IStreamListenerManager
    {
        void ForceSnapshot(string fixtureId);
        IObservable<IFixtureOverviewDelta> GetFixtureOverviewStream();
        IEnumerable<IFixtureOverview> GetFixtures();
        FixtureOverview GetFixtureOverview(string fixtureId);

        void RemoveFixtureEventState(string fixtureId);

        void Initialise();
    }
}
