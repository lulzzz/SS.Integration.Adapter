using System;
using System.Runtime.Serialization;
using System.Security;
using SS.Integration.Adapter.Interface;

namespace SS.Integration.Adapter
{
    public class StreamListenerEventArgs : EventArgs
    {
        public IListener Listener { get; set; }
        public Exception Exception { get; set; }
        public int Epoch { get; set; }
        public int CurrentSequence { get; set; }
        public DateTime? StartTime { get; set; }

        public bool IsSnapshot { get; set; }
        public string CompetitionId { get; set; }
        public string CompetitionName { get; set; }
        
    }
}