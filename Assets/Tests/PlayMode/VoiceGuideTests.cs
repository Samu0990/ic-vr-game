using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRSurgery.Feedback;
using VRSurgery.Surgery;
using VRSurgery.Transplant;

namespace VRSurgery.Tests
{
    /// <summary>One voice at a time, emergencies first, no nagging, silence where there is no recording.</summary>
    public class VoiceGuideTests
    {
        private readonly List<Object> _spawned = new List<Object>();
        private readonly List<string> _said = new List<string>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) { Object.DestroyImmediate(o); }
            }

            _spawned.Clear();
            _said.Clear();
            SurgeryEvents.ResetAll();
        }

        private VoiceGuide Guide()
        {
            GameObject host = new GameObject("Voice");
            _spawned.Add(host);
            VoiceGuide guide = host.AddComponent<VoiceGuide>();
            foreach (string id in new[] { "a", "b", "c", "sangrando" })
            {
                AudioClip clip = AudioClip.Create(id, 44100, 1, 44100, false);
                _spawned.Add(clip);
                guide.Register(id, clip);
            }

            guide.Said += _said.Add;
            return guide;
        }

        [Test]
        public void LinesWaitTheirTurnInsteadOfTalkingOverEachOther()
        {
            VoiceGuide guide = Guide();

            guide.Say("a");
            guide.Say("b");
            Assert.AreEqual(new[] { "a" }, _said.ToArray());
            Assert.AreEqual(1, guide.Queued);

            guide.Tick(0.5f);
            Assert.AreEqual(1, _said.Count, "Still saying the first.");

            guide.Tick(0.8f);
            Assert.AreEqual(new[] { "a", "b" }, _said.ToArray());
        }

        [Test]
        public void AnEmergencyCutsIn()
        {
            VoiceGuide guide = Guide();

            guide.Say("a");
            guide.Say("b");
            guide.Say("sangrando", true);

            Assert.AreEqual(new[] { "a", "sangrando" }, _said.ToArray());
            Assert.AreEqual(0, guide.Queued, "What was waiting is dropped: the leak matters now.");
        }

        [Test]
        public void TheSameLineIsNotRepeatedStraightAway()
        {
            VoiceGuide guide = Guide();

            guide.Say("a");
            guide.Tick(2f);
            guide.Say("a");
            Assert.AreEqual(1, _said.Count, "No nagging.");

            guide.Tick(9f);
            guide.Say("a");
            Assert.AreEqual(2, _said.Count, "Later, it can be said again.");
        }

        [Test]
        public void ALineWithNoRecordingIsSkippedSilently()
        {
            VoiceGuide guide = Guide();
            guide.Say("nao_gravada");
            Assert.AreEqual(0, _said.Count);
        }

        [Test]
        public void EveryStageTheVisitorWorksHasALine()
        {
            foreach (TransplantStage stage in new[]
                     {
                         TransplantStage.SkinIncision, TransplantStage.OpenChest, TransplantStage.OpenPericardium,
                         TransplantStage.GoOnBypass, TransplantStage.RemoveNativeHeart, TransplantStage.PlaceDonorHeart,
                         TransplantStage.ConnectVessels, TransplantStage.Restart, TransplantStage.CloseSkin,
                         TransplantStage.Complete,
                     })
            {
                Assert.IsNotNull(VoiceGuide.StageLine(stage), stage.ToString());
            }
        }
    }
}
