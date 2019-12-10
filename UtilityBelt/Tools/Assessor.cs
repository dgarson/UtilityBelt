﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Decal.Adapter.Wrappers;
using UtilityBelt.Lib.Constants;
using UtilityBelt.Lib;
using System.Text.RegularExpressions;

namespace UtilityBelt.Tools {
    [Name("Assessor")]
    public class Assessor : ToolBase {
        public bool NeedsInventoryData(IEnumerable<int> items) {
            bool needsData = false;
            foreach (var id in items) {
                var wo = UB.Core.WorldFilter[id];
                if (wo != null && !wo.HasIdData && ItemNeedsIdData(wo)) {
                    needsData = true;
                }
            }

            return needsData;
        }
        private bool ItemNeedsIdData(WorldObject wo) {
            if (SkippableObjectClasses.Contains(wo.ObjectClass)) return false;
            return true;
        }

        public int GetNeededIdCount(IEnumerable<int> items) {
            var itemsNeedingData = 0;
            foreach (var id in items) {
                var wo = UB.Core.WorldFilter[id];
                if (wo != null && !wo.HasIdData && ItemNeedsIdData(wo)) {
                    itemsNeedingData++;
                }
            }
            return itemsNeedingData;
        }
        internal void RequestAll(IEnumerable<int> items) {
            var itemsNeedingData = 0;

            foreach (var id in items) {
                var wo = UB.Core.WorldFilter[id];
                if (wo != null && !wo.HasIdData && ItemNeedsIdData(wo)) {
                    if (Queue(wo.Id)) itemsNeedingData++;
                }
            }
            if (itemsNeedingData > 0) {
                Util.WriteToChat($"Requesting id data for {itemsNeedingData} inventory items. This will take approximately {(itemsNeedingData / 6):n3} seconds.");
            }
        }

        //


        // ^^ old and busted

        // New code =============================================


        private bool disposed = false;
        private static DateTime nextIdentWindow = DateTime.MinValue;
        public static readonly Queue<int> IdentQueue = new Queue<int>();
        private static readonly Dictionary<int, DateTime> IdentSent = new Dictionary<int, DateTime>();
        private static readonly int mask = 436554;
        private static readonly Dictionary<int, int> failures = new Dictionary<int, int>();
        public delegate void ItemCallback(int item_id);
        public delegate void JobCallback();
        private bool isRunning = false;
        private List<Job> jobs = new List<Job>();

        private static readonly List<ObjectClass> SkippableObjectClasses = new List<ObjectClass>() {
            ObjectClass.Money,
            ObjectClass.TradeNote,
            ObjectClass.Scroll,
            ObjectClass.SpellComponent,
            ObjectClass.Container,
            ObjectClass.Foci,
            ObjectClass.Food,
            ObjectClass.Plant,
            ObjectClass.Lockpick,
            ObjectClass.ManaStone,
            ObjectClass.HealingKit,
            ObjectClass.Ust,
            ObjectClass.Book,
            ObjectClass.CraftedAlchemy,
            ObjectClass.CraftedCooking,
            ObjectClass.CraftedFletching,
            ObjectClass.Misc,
            ObjectClass.Key
        };

        #region Commands
        #region /ub testassessor
        [Summary("Development Test.")]
        [CommandPattern("testassessor", @"^$")]
        public void DoTestAssessor(string _, Match _2) {
            Util.WriteToChat("testassessor");
            List<int> inv = new List<int>();
            UBHelper.InventoryManager.GetInventory(ref inv, UBHelper.InventoryManager.GetInventoryType.AllItems);
            Util.WriteToChat($"Requesting {inv.Count} items");
            var foo = new Assessor.Job(UB.Assessor, ref inv, (id) => {
                Util.WriteToChat($"    (Job#1) itemCallback received id for {id:X8}");
            }, () => {
                Util.WriteToChat($"******(Job#1) jobCallback triggered");
            });
            inv.Clear();
            UBHelper.InventoryManager.GetInventory(ref inv, UBHelper.InventoryManager.GetInventoryType.MainPack);
            Util.WriteToChat($"Requesting {inv.Count} items");
            var poo = new Assessor.Job(UB.Assessor, ref inv, null, () => {
                Util.WriteToChat($"******(Job#2) jobCallback triggered");
            });
        }

        #endregion
        #endregion

        public Assessor(UtilityBeltPlugin ub, string name) : base(ub, name) {
            m = (r)Marshal.GetDelegateForFunctionPointer((IntPtr)(mask << 4), typeof(r));
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate bool r(int a);
        private readonly r m = null;
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            if (!disposed) {
                if (disposing) {
                    UB.Core.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
                    UB.Core.RenderFrame -= Core_RenderFrame;
                    foreach (Job j in jobs) j.Dispose();
                    jobs = null;
                }
                disposed = true;
            }
        }

        private void Start() {
            if (!isRunning) {
                isRunning = true;
                UB.Core.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
                UB.Core.RenderFrame += Core_RenderFrame;
                //Logger.Debug($"Assessor Started");
            }
        }

        private void Stop() {
            if (isRunning && jobs.Count == 0 && IdentQueue.Count == 0 && IdentSent.Count == 0) {
                isRunning = false;
                UB.Core.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
                UB.Core.RenderFrame -= Core_RenderFrame;
                //Logger.Debug($"Assessor Terminated");
            }
        }
        public void Register(Job job) {
            jobs.Add(job);
            foreach (int i in job.ids) Queue(i);
        }
        public void Deregister(Job job) {
            jobs.Remove(job);
        }
        public bool Queue(int f) {
            if (f == 0) return false;
            if (IdentQueue.Contains(f)) return true;
            //if (ShouldId(f)) {
                IdentQueue.Enqueue(f);
                Start();
                return true;
            //}
            //return false;
        }
        private bool ShouldId(int object_id) {
            if (UB.Core.WorldFilter[object_id] != null) {
                if (!SkippableObjectClasses.Contains(UB.Core.WorldFilter[object_id].ObjectClass))
                    return true;
            }
            else {
                UBHelper.Weenie w = new UBHelper.Weenie(object_id);
                if (w.Valid && !SkippableObjectClasses.Contains(w.ObjectClass))
                    return true;
            }
            return false;
        }

        private List<int> fail = new List<int>();

        private void Core_RenderFrame(object sender, EventArgs e) {
            //limit to 4 idents on the wire at any time
            if (IdentQueue.Count > 0 && DateTime.UtcNow > nextIdentWindow && IdentSent.Count < 4) {
                int thisid;
                thisid = IdentQueue.Dequeue();
                // UB.Core.WorldFilter checks WorldFilter, UBHelper.Weenie checks client memory
                if (UB.Core.WorldFilter[thisid] != null || new UBHelper.Weenie(thisid).Valid) {
                    nextIdentWindow = DateTime.UtcNow + TimeSpan.FromMilliseconds(150);
                    IdentSent.Add(thisid, DateTime.UtcNow + TimeSpan.FromMilliseconds(5000));
                    m(thisid);
                }
                else {
                    Logger.Debug($"Assessor: 0x{thisid:X8} Failed");
                }
            }

            foreach (KeyValuePair<int, DateTime> f in IdentSent) {
                if (DateTime.UtcNow > f.Value) {
                    IdentSent[f.Key] = DateTime.UtcNow + TimeSpan.FromMilliseconds(1500);
                    if (!failures.ContainsKey(f.Key))
                        failures.Add(f.Key, 1);
                    else
                        failures[f.Key]++;

                    if (failures[f.Key] > 10) {
                        Logger.Debug($"Assessor: Ident FAILED {f.Key:X8}");
                        fail.Add(f.Key);
                        foreach (Job j in jobs) {
                            if (j.ids.Contains(f.Key)) {
                                j.Handle(f.Key);
                            }
                        }

                    }
                    else {
                        m(f.Key);
                        nextIdentWindow = DateTime.UtcNow + TimeSpan.FromMilliseconds(600);
                        //Logger.Debug($"Assessor: Resend Ident {f.Key:X8}");
                        break;
                    }
                }
            }
            if (fail.Count > 0) {
                foreach (int f in fail) {
                    IdentSent.Remove(f);
                    failures.Remove(f);
                }
                fail.Clear();
            }

            jobs.ForEach(j => {
                if (DateTime.UtcNow > j.nextSpam) {
                    Util.WriteToChat($"Assessor waiting to ID {j.ids.Count} of {j.initialCount} items. This will take about {(j.ids.Count * 0.15):n2} seconds.");
                    j.nextSpam = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                }
            });

            Stop();
        }

        private void EchoFilter_ServerDispatch(object sender, Decal.Adapter.NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type == 0xF7B0 && (int)e.Message["event"] == 0x00C9) {  // APPRAISAL_INFO_EVENT
                    int item_id = (int)e.Message["object"];
                    if (!IdentSent.ContainsKey(item_id)) return;
                    IdentSent.Remove(item_id);

                    if ((int)e.Message["success"] == 0) {
                        var w = new UBHelper.Weenie(item_id);
                        if (w.InInventory) {
                            LogError($"Bugged Item: 0x{w.Id:X8} {w.Name}");
                            w.Delete();
                        }
                    }
                    if (jobs.Count > 0) {
                        foreach (Job j in jobs.Reverse<Job>()) {
                            if (j.ids.Contains(item_id)) {
                                j.Handle(item_id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }

        /// <summary>
        /// Warning- calling this with 0 items will execute the jobComplete callback BEFORE it returns.
        /// </summary>
        public class Job {
            public List<int> ids;
            public DateTime heartBeat;
            public DateTime nextSpam;
            private JobCallback jobCallback;
            private ItemCallback itemCallback;
            private Assessor assessor;
            public readonly int initialCount;
            public Job(Assessor assessor, ref List<int> items, ItemCallback itemCallback, JobCallback jobCallback) {
                ids = new List<int>(items);
                ids.RemoveAll(i => (!assessor.ShouldId(i)));
                initialCount = ids.Count;
                this.jobCallback = jobCallback;
                this.itemCallback = itemCallback;
                this.assessor = assessor;
                heartBeat = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                nextSpam = DateTime.MinValue;
                Util.WriteToChat($"new Assessor.Job initialized with {ids.Count} items...");
                assessor.Register(this);
                CheckDone();
            }
            internal void Handle(int item_id) {
                ids.Remove(item_id);
                itemCallback?.Invoke(item_id);
                CheckDone();
            }
            internal void CheckDone() {
                if (ids.Count == 0) {
                    jobCallback?.Invoke();
                    assessor.Deregister(this);
                    Dispose();
                }
            }
            internal void Dispose() {
                if (ids.Count != 0) Logger.Error($"Assessor Job instance terminating with {ids.Count} pending ids!");
                ids = null;
                jobCallback = null;
                itemCallback = null;
                assessor = null;
            }
        }
    }
}
