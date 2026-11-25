using NUnit.Framework;
using PoBox.Audio;
using UnityEngine;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The 2026-11-25 audio layer: the procedural voices' mouth stream (lip-sync) and the modal impact sets.
    /// </summary>
    public class ReactiveAudioTests
    {
        [Test]
        public void EveryProceduralTakeHasAMouthThatMovesAndShutsAtTheEnd()
        {
            VoiceProfile matt = VoiceProfile.For("MATT", 80f, false);
            foreach (Emotion5 e in System.Enum.GetValues(typeof(Emotion5)))
            {
                float[] data = VoiceSynth.Render5(matt, e, 4, 0, "matt", out VisemeTrack mouth);
                Assert.IsNotNull(mouth, $"{e}: no mouth stream");
                Assert.AreEqual((data.Length + VisemeTrack.DefaultHop - 1) / VisemeTrack.DefaultHop, mouth.Blocks, $"{e}: blocks do not cover the take");
                float top = 0f;
                for (int b = 0; b < mouth.Blocks; b++) top = Mathf.Max(top, mouth.OpenAt(b));
                Assert.Greater(top, 0.2f, $"{e}: the mouth never opens");
                mouth.Sample(data.Length + 10 * VisemeTrack.DefaultHop, out float open, out _);
                Assert.AreEqual(0f, open, $"{e}: the mouth is open after the take");
                // The mouth stream does not change the sound.
                CollectionAssert.AreEqual(VoiceSynth.Render5(matt, e, 4, 0, "matt"), data, $"{e}: the take changed");
            }
        }

        [Test]
        public void TheOlderEmotionsHaveAMouthToo()
        {
            VoiceProfile kim = VoiceProfile.For("KIM", 60f, true);
            foreach (Emotion e in System.Enum.GetValues(typeof(Emotion)))
            {
                float[] data = VoiceSynth.Render(kim, e, new System.Random(3), out VisemeTrack mouth);
                Assert.IsNotNull(mouth, $"{e}");
                Assert.GreaterOrEqual(mouth.Blocks * mouth.HopSamples, data.Length, $"{e}");
            }
        }

        [Test]
        public void AnAhIsMoreOpenThanAnOoAndAnEeIsWiderThanAnOo()
        {
            var ah = MouthShape.Vowel(1, new Vector3(730f, 1090f, 2440f), new Vector3(730f, 1090f, 2440f), 1f);
            var oo = MouthShape.Vowel(1, new Vector3(320f, 870f, 2240f), new Vector3(320f, 870f, 2240f), 1f);
            var ee = MouthShape.Vowel(1, new Vector3(300f, 2250f, 2950f), new Vector3(300f, 2250f, 2950f), 1f);
            Assert.Greater(ah.open0, oo.open0);
            Assert.Greater(ee.width0, oo.width0);
        }

        [Test]
        public void TheModalSetsCoverEveryContactAndHarderIsLouder()
        {
            var settings = ScriptableObject.CreateInstance<ImpactModalSettings>();
            try
            {
                foreach (ContactKind k in System.Enum.GetValues(typeof(ContactKind)))
                {
                    ModalContactSet set = settings.For(k);
                    Assert.IsNotNull(set, $"{k}");
                    Assert.Greater(set.modes.Length, 0, $"{k}: no modes");
                    Assert.Less(set.forceRange.x, set.forceRange.y, $"{k}");
                    Assert.Less(set.speedRange.x, set.speedRange.y, $"{k}");
                    Assert.LessOrEqual(set.bodyVolume.x, set.bodyVolume.y, $"{k}");
                }
                Assert.Greater(settings.head.modes[0].hz, settings.torso.modes[0].hz, "the skull should ring above the trunk");
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}
