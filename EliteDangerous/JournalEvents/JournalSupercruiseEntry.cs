﻿/*
 * Copyright © 2016 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 *
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */
using Newtonsoft.Json.Linq;
using System.Linq;

namespace EliteDangerousCore.JournalEvents
{
    //When written: entering supercruise from normal space
    //Parameters:
    //•	Starsystem
    [JournalEntryType(JournalTypeEnum.SupercruiseEntry)]
    public class JournalSupercruiseEntry : JournalEntry
    {
        public JournalSupercruiseEntry(JObject evt ) : base(evt, JournalTypeEnum.SupercruiseEntry)
        {
            StarSystem = evt["StarSystem"].Str();
            SystemAddress = evt["SystemAddress"].LongNull();

        }
        public string StarSystem { get; set; }
        public long? SystemAddress { get; set; }

        public override void FillInformation(out string info, out string detailed)//V
        {
            
            info = StarSystem;
            detailed = "";
        }
    }
}
