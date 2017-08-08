﻿//Copyright 2017 Spin Services Limited

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
using System.IO;
using System.Reflection;
using Akka.Actor;
using log4net;
using Newtonsoft.Json;
using SS.Integration.Adapter.Actors.Messages;
using SS.Integration.Adapter.Interface;
using SS.Integration.Adapter.Model;
using SS.Integration.Adapter.Model.Interfaces;

namespace SS.Integration.Adapter.Actors
{
    /// <summary>
    /// This actor class has the responsability to manage fixtures state.
    /// The state is kept in memory as dictionary and is automatically saved to disk by self scheduled message at pre-defined interval.
    /// </summary>
    public class FixtureStateActor : ReceiveActor
    {
        #region Constructors

        public const string ActorName = nameof(FixtureStateActor);
        public const string Path = "/user/" + ActorName;

        #endregion

        #region Fields

        private readonly ILog _logger = LogManager.GetLogger(typeof(FixtureStateActor));
        private readonly ISettings _settings;
        private readonly IStoreProvider _storeProvider;
        private string _pathFileName;
        private Dictionary<string, FixtureState> _fixturesState;

        #endregion

        #region Constructors

        public FixtureStateActor(ISettings settings, IStoreProvider storeProvider)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _storeProvider = storeProvider ?? throw new ArgumentNullException(nameof(storeProvider));

            _fixturesState = new Dictionary<string, FixtureState>();

            SetFilePath();
            LoadStateFile();

            Context.System.Scheduler.ScheduleTellRepeatedly(
                _settings.FixturesStateAutoStoreInterval,
                _settings.FixturesStateAutoStoreInterval,
                Self,
                new WriteStateToFileMsg(),
                Self);

            Receive<GetFixtureStateMsg>(a => GetFixtureStateMsgHandler(a));
            Receive<UpdateFixtureStateMsg>(a => UpdateFixtureStateMsgHandler(a));
            Receive<RemoveFixtureStateMsg>(a => RemoveFixtureStateMsgHandler(a));
            Receive<WriteStateToFileMsg>(a => WriteStateToFileMsgHandler(a));
        }

        #endregion

        #region Message handlers

        private void GetFixtureStateMsgHandler(GetFixtureStateMsg msg)
        {
            FixtureState fixtureState;
            _fixturesState.TryGetValue(msg.FixtureId, out fixtureState);
            Sender.Tell(fixtureState, Self);
        }

        private void UpdateFixtureStateMsgHandler(UpdateFixtureStateMsg msg)
        {
            _logger.Debug($"Updating state for Fixture fixtureId={msg.FixtureId} sequence={msg.Sequence}");

            FixtureState fixtureState;
            if (!_fixturesState.TryGetValue(msg.FixtureId, out fixtureState))
            {
                fixtureState = new FixtureState { Id = msg.FixtureId, Sport = msg.Sport };
            }

            fixtureState.Sequence = msg.Sequence;
            fixtureState.MatchStatus = msg.Status;
            fixtureState.Epoch = msg.Epoch;

            _fixturesState[msg.FixtureId] = fixtureState;
        }

        private void RemoveFixtureStateMsgHandler(RemoveFixtureStateMsg msg)
        {
            _fixturesState.Remove(msg.FixtureId);
        }

        private void WriteStateToFileMsgHandler(WriteStateToFileMsg msg)
        {
            try
            {
                var output = JsonConvert.SerializeObject(_fixturesState, Formatting.Indented);
                _storeProvider.Write(_pathFileName, output);
            }
            catch (Exception ex)
            {
                _logger.Error("Error when writting State to File", ex);
            }
        }

        #endregion

        #region Private methods

        private void SetFilePath()
        {
            if (System.IO.Path.IsPathRooted(_settings.FixturesStateFilePath))
            {
                _pathFileName = _settings.FixturesStateFilePath;
            }
            else
            {
                var path = Assembly.GetExecutingAssembly().Location;
                var fileInfo = new FileInfo(path);
                var dir = fileInfo.DirectoryName;
                _pathFileName = System.IO.Path.Combine(dir, _settings.FixturesStateFilePath);
            }
        }

        private void LoadStateFile()
        {
            try
            {
                _logger.InfoFormat("Attempting to load file from filePath {0}", _pathFileName);

                var savedFixtureStates = File.Exists(_pathFileName) ? _storeProvider.Read(_pathFileName) : null;
                if (!string.IsNullOrWhiteSpace(savedFixtureStates))
                {
                    _fixturesState = (Dictionary<string, FixtureState>)
                        JsonConvert.DeserializeObject(savedFixtureStates, typeof(Dictionary<string, FixtureState>));
                }
            }
            catch (JsonSerializationException jse)
            {
                _logger.Error($"Error during state deserialization {jse}. The state file will be removed!");
                DeleteStateFile();
            }
        }

        private void DeleteStateFile()
        {
            if (File.Exists(_pathFileName))
            {
                File.Delete(_pathFileName);
                _logger.DebugFormat($"Deleted state file {_pathFileName}");
            }
        }

        #endregion

        #region Private messages

        private class WriteStateToFileMsg
        {
        }

        #endregion
    }
}