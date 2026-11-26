using System;
using Mujoco;
using UnityEngine;

namespace PoBox.Mj
{
    /// <summary>
    /// Where a boxer is put: stood in its guard on a spot, held for the referee's count, let go, sat between rounds,
    /// walked on and off, told it is down or rising. The ring's own stepping, punching and measuring are in
    /// <c>MjRing.cs</c>; this half only moves boxers and sets flags.
    /// </summary>
    public unsafe partial class MjRing
    {
        /// <summary>Stands a boxer in its guard on a spot (Unity), facing a way, with nothing moving. The other is left as it is.</summary>
        public void Place(MjBoxer b, Vector3 floorPoint, Vector3 facing)
        {
            if (!Ready) return;
            int k = Slot(b);
            b.ResetToGuard(floorPoint.x, floorPoint.z, Math.Atan2(facing.z, facing.x));
            b.SetEpisodeAt(MjBoxer.Match, 0, 0, 0, 0, 0);
            b.DriveScale = 1.0;
            // The reserve is left as it is: a boxer stood up by the referee is as tired as it was. The bout fills it (Fighter.Respawn).
            _held[k] = false;
            _pose[k] = null;
            SetGhost(k, false);
            Forget(k);                      // the other's target has just jumped too
            MjScene.Instance.SyncUnityToMjState();
        }

        /// <summary>
        /// Holds a boxer in its guard where it stands, looking a way (Unity), until it is let go: the boxer left
        /// standing while the referee counts over the other. Let go facing its opponent a step and a half away
        /// with nothing moving, it is at exactly the first moment of a training episode.
        /// </summary>
        public void Hold(MjBoxer b, Vector3 facing)
        {
            if (!Ready) return;
            int k = Slot(b);
            if (!_held[k])
            {
                _held[k] = true;
                _pose[k] = null;
                double limit = ringHalf - 0.5;          // a boxer caught on the ropes is stood a step in
                var pelvis = b.transform.Find(b.prefix + "pelvis");
                Vector3 at = pelvis != null ? pelvis.position : b.transform.position;
                _holdX[k] = Math.Max(centre.x - limit, Math.Min(centre.x + limit, at.x));
                _holdY[k] = Math.Max(centre.y - limit, Math.Min(centre.y + limit, at.z));
            }
            if (facing.x * facing.x + facing.z * facing.z > 1e-4f) _holdYaw[k] = Math.Atan2(facing.z, facing.x);
        }

        /// <summary>
        /// Holds a boxer in a pose (MjBoxer.Pose) on a spot (Unity), facing a way, until it is placed or let go: on its
        /// stool between rounds, the winner's gloves up. Put there now, so it shows with the ring stopped too.
        /// </summary>
        public void HoldPose(MjBoxer b, Vector3 floorPoint, Vector3 facing, double[] joints)
        {
            if (!Ready) return;
            int k = Slot(b);
            _held[k] = true;
            _pose[k] = joints;
            _holdX[k] = floorPoint.x;
            _holdY[k] = floorPoint.z;
            if (facing.x * facing.x + facing.z * facing.z > 1e-4f) _holdYaw[k] = Math.Atan2(facing.z, facing.x);
            Rehold(k);
            Forget(k);
            MjScene.Instance.SyncUnityToMjState();
        }

        /// <summary>
        /// A boxer held in a pose (HoldPose) takes another where it stands, facing the same way: the winner's celebration,
        /// a new pose each frame. Every step re-places it in the latest one, as it does with any held pose. Nothing
        /// happens to a boxer not held in a pose.
        /// </summary>
        public void ChangeHeldPose(MjBoxer b, double[] joints)
        {
            if (!Ready) return;
            int k = Slot(b);
            if (!_held[k] || _pose[k] == null) return;
            _pose[k] = joints;
            Rehold(k);
        }

        /// <summary>A held boxer back on its spot, in its pose or its guard.</summary>
        void Rehold(int k)
        {
            if (_pose[k] != null) _b[k].ResetToPose(_holdX[k], _holdY[k], _holdYaw[k], _pose[k]);
            else _b[k].ResetToGuard(_holdX[k], _holdY[k], _holdYaw[k]);
        }
        public void Release(MjBoxer b)
        {
            int k = Slot(b);
            _held[k] = false;
            _pose[k] = null;
            if (_b != null) Forget(k);
        }

        /// <summary>
        /// A boxer parked off stage (a free-for-all's boxer waiting to come in, or one counted out and cleared): held in
        /// its pose every step, so its brain is not run at all.
        /// </summary>
        public void SetIdle(MjBoxer b, bool idle) { if (_idle != null) _idle[Slot(b)] = idle; }
        /// <summary>
        /// Walking out after the result (MjBrain.LeaveRing): the boxer meets the floor and nothing else, so it goes out
        /// through the ropes and past anybody in its way. Off again when the bout places it.
        /// </summary>
        public void SetLeaving(MjBoxer b, bool on)
        {
            if (_leaving == null) return;
            int k = Slot(b);
            if (_leaving[k] == on) return;
            _leaving[k] = on;
            if (_geoms[k] == null) return;
            // Back off it: layer 1 is the standing, not-a-ghost layer of a pair, and _body -1 has SetBody write its own.
            _body[k] = -1;
            _ghost[k] = false;
            int layer = on ? Leaving : 1;
            foreach (int g in _geoms[k]) { _m->geom_contype[g] = layer; _m->geom_conaffinity[g] = layer; }
        }

        /// <summary>The bout says whether a boxer is down: one that is down is passed through by the other.</summary>
        public void SetDown(MjBoxer b, bool down) => _down[Slot(b)] = down;
        /// <summary>The bout says whether a boxer down is getting up (its get-up policy has the body).</summary>
        public void SetRising(MjBoxer b, bool rising) => _rising[Slot(b)] = rising;
    }
}
