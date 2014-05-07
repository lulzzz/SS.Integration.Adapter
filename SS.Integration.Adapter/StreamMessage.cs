//Copyright 2014 Spin Services Limited

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//    http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using Newtonsoft.Json;
using SS.Integration.Common;

namespace SS.Integration.Adapter
{
    public class StreamMessage
    {
        public string Relation { get; set; }
        public object Content { get; set; }

        public T GetContent<T>()
        {
            return JsonConvert.DeserializeObject<T>(Content.ToString(), FixtureDateTimeJsonConverter.Instance);
        }
    }
}
