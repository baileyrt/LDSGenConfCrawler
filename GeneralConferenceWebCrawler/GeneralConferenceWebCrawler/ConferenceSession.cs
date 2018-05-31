using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeneralConferenceWebCrawler
{
    class ConferenceSession
    {
        public int Year { get; set; }
        public string Month { get; set; }
        public string Title { get; set; }
        public List<ConferenceTalk> Talks { get; set; }
    }
}
