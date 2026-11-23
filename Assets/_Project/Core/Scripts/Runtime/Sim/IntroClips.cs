using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PoBox.Sim
{
    /// <summary>
    /// The walk-on moves: a few seconds each of shadow boxing from the CMU motion-capture database, put on the boxers'
    /// 21 joints by Training/tools/intro_clips.py into Resources/IntroClips.bytes. Nothing is trained: IntroMoves
    /// plays them straight onto the bodies while the walk-on has the physics stopped.
    ///
    /// The file (little-endian): "PBIC", int32 version 1, float32 fps, int32 joints, each joint's name (int32 length,
    /// UTF-8), int32 clips, and each clip's name, int32 frames and frames x (7 + joints) float32. A row is the root's
    /// x, y, z in leg lengths, its quaternion (w x y z), and the joint angles; a clip starts at the origin facing +x.
    /// </summary>
    public class IntroClips
    {
        public const string ResourceName = "IntroClips";
        public const int Version = 1;

        public float Fps { get; private set; }
        public string[] Joints { get; private set; }
        /// <summary>Numbers a row: the root's seven and one a joint.</summary>
        public int Width => 7 + Joints.Length;
        public readonly List<string> Names = new List<string>();
        readonly Dictionary<string, float[]> _rows = new Dictionary<string, float[]>();

        public float[] Rows(string clip) => _rows.TryGetValue(clip, out float[] r) ? r : null;
        public int Frames(string clip) => _rows.TryGetValue(clip, out float[] r) ? r.Length / Width : 0;
        public float Seconds(string clip) => Frames(clip) / Fps;

        /// <summary>The game's copy, from Resources; null when there is none.</summary>
        public static IntroClips Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            return asset != null ? Parse(asset.bytes) : null;
        }

        public static IntroClips Parse(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "PBIC") throw new InvalidDataException("not an intro clip file");
            int version = r.ReadInt32();
            if (version != Version) throw new InvalidDataException($"intro clip file version {version}, expected {Version}");
            var set = new IntroClips { Fps = r.ReadSingle() };
            set.Joints = new string[r.ReadInt32()];
            for (int j = 0; j < set.Joints.Length; j++) set.Joints[j] = Text(r);
            int clips = r.ReadInt32();
            for (int c = 0; c < clips; c++)
            {
                string name = Text(r);
                var rows = new float[r.ReadInt32() * set.Width];
                for (int i = 0; i < rows.Length; i++) rows[i] = r.ReadSingle();
                set.Names.Add(name);
                set._rows[name] = rows;
            }
            if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException("trailing bytes in the intro clip file");
            return set;
        }

        static string Text(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
    }
}
