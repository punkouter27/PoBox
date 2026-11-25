using System;
using Mujoco;

namespace PoBox.Mj
{
    /// <summary>
    /// The observation pass (the 103 numbers, then the three a wide brain reads): the same order and the same
    /// arithmetic as <c>Training/tools/footwork_c.py</c>'s <c>observe()</c>. Split out of MjBoxer.cs, which keeps the
    /// model, the brain and the drive; nothing here does anything else.
    /// </summary>
    public unsafe partial class MjBoxer
    {
        /// <summary>The vector from `from` to `p`, in the frame that has turned yaw about the vertical.</summary>
        static void Heading(double* p, double* from, double yaw, double[] o, int at = 0)
        {
            double x = p[0] - from[0], y = p[1] - from[1], c = Math.Cos(yaw), s = Math.Sin(yaw);
            o[at] = c * x + s * y; o[at + 1] = -s * x + c * y; o[at + 2] = p[2] - from[2];
        }

        /// <summary>The opponent's stand-in: its head, body and gloves in the world, facing the boxer. A walk's is carried dead ahead.</summary>
        void StandIn()
        {
            double* pos = _d->xpos + 3 * _pelvis;
            double yaw = Yaw(_d->xquat + 4 * _pelvis);
            if ((Kind == Match || Kind == GetUp) && opponent != null && opponent.Bound)
            {
                // A bout: the other boxer, as it is.
                _theirs = Yaw(_d->xquat + 4 * opponent._pelvis);
                for (int p = 0; p < 4; p++)
                {
                    int geom = p == 0 ? opponent._head : p == 1 ? opponent._torso : opponent._glove[p - 2];
                    for (int k = 0; k < 3; k++) _standIn[p][k] = _d->geom_xpos[3 * geom + k];
                }
                return;
            }
            double ox = Kind == Walk ? pos[0] + Math.Cos(yaw) * 1.2 : Opp[0], oy = Kind == Walk ? pos[1] + Math.Sin(yaw) * 1.2 : Opp[1];
            _theirs = Math.Atan2(pos[1] - oy, pos[0] - ox);
            double c = Math.Cos(_theirs), s = Math.Sin(_theirs);
            for (int p = 0; p < 4; p++)
            {
                double[] g = _guard[p], o = _standIn[p];
                o[0] = ox + c * g[0] - s * g[1]; o[1] = oy + s * g[0] + c * g[1]; o[2] = Config.stand_height + g[2];
            }
        }

        /// <summary>The 103 numbers, from mjData as it stands. Same order and same arithmetic as footwork_c.py's observe().</summary>
        public void Observe()
        {
            double* pos = _d->xpos + 3 * _pelvis, R = _d->xmat + 9 * _pelvis, lin = _d->qvel + _rootV;
            double yaw = Yaw(_d->xquat + 4 * _pelvis);
            double[] o = Obs;
            for (int i = 0; i < 3; i++)
            {
                o[i] = R[i] * lin[0] + R[3 + i] * lin[1] + R[6 + i] * lin[2];      // the pelvis's velocity, as it feels it
                o[3 + i] = lin[3 + i];
                o[6 + i] = -R[6 + i];                                              // which way is down
            }
            int n = Joints;
            for (int i = 0; i < n; i++)
            {
                o[9 + i] = _d->qpos[_jq[i]] - Config.default_joint_pos[i];
                o[9 + n + i] = _d->qvel[_jv[i]];
                o[9 + 2 * n + i] = Action[i];
            }
            int at = 9 + 3 * n;
            for (int f = 0; f < 2; f++)
            {
                double* gp = _d->geom_xpos + 3 * _foot[f], gm = _d->geom_xmat + 9 * _foot[f], size = _m->geom_size + 3 * _foot[f];
                double sole = gp[2] - (Math.Abs(gm[6]) * size[0] + Math.Abs(gm[7]) * size[1] + Math.Abs(gm[8]) * size[2]);
                o[at++] = sole < 0.005 ? 1.0 : 0.0;
            }
            o[at++] = pos[2];
            StandIn();
            fixed (double* head = _standIn[0], body = _standIn[1], gl = _standIn[2], gr = _standIn[3])
            {
                Heading(head, pos, yaw, o, at); at += 3;
                Heading(body, pos, yaw, o, at); at += 3;
                double vx = _headVel[0] - lin[0], vy = _headVel[1] - lin[1], c = Math.Cos(yaw), s = Math.Sin(yaw);
                o[at++] = c * vx + s * vy; o[at++] = -s * vx + c * vy; o[at++] = _headVel[2] - lin[2];
                Heading(_d->geom_xpos + 3 * _glove[0], pos, yaw, o, at); at += 3;
                Heading(_d->geom_xpos + 3 * _glove[1], pos, yaw, o, at); at += 3;
                Heading(gl, pos, yaw, o, at); at += 3;
                Heading(gr, pos, yaw, o, at); at += 3;
            }
            o[at++] = Math.Cos(_theirs - yaw); o[at++] = Math.Sin(_theirs - yaw);
            double cy = Math.Cos(yaw), sy = Math.Sin(yaw);
            double rx = ringCentre.x - pos[0], ry = ringCentre.y - pos[1];
            // A bout's ring is the trainer's (envs/boxing.py: not held to its size); the footwork stage holds it to one.
            double reach = Kind == Match || Kind == GetUp ? 100.0 : 1.0;
            o[at++] = Clamp((cy * rx + sy * ry) / Config.ring_half, -reach, reach);
            o[at++] = Clamp((-sy * rx + cy * ry) / Config.ring_half, -reach, reach);
            // In a bout the command is zeros; a policy trained with obs_state reads its own state there (envs/match.py).
            bool state = Kind == Match && Config.obs_state;
            o[at++] = state ? Reserve : Cmd[0]; o[at++] = state ? DazeShare : Cmd[1]; o[at++] = state ? OpponentDazeShare : Cmd[2];
            for (int i = 0; i < ObservationSize; i++)
            {
                if (!double.IsFinite(o[i])) { NanInfCount++; o[i] = 0.0; }
                else o[i] = Clamp(o[i], -100.0, 100.0);
                _obsF[i] = (float)o[i];      // the policy's copy, written here so Infer has nothing left to convert
            }
            Bodies(pos, yaw, o, ObservationSize);
            if (Rules.LagMax > 0) Delay();
        }

        /// <summary>The bodies' three numbers (see <see cref="BodyInputs"/>): zeros with nobody down within 3 m.</summary>
        void Bodies(double* pos, double yaw, double[] o, int at)
        {
            o[at] = o[at + 1] = o[at + 2] = 0.0;
            if (NearestDownPelvis < 0) return;
            double* p = _d->xpos + 3 * NearestDownPelvis;
            double dx = p[0] - pos[0], dy = p[1] - pos[1];
            if (dx * dx + dy * dy > 9.0) return;
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            o[at] = Clamp(c * dx + s * dy, -3.0, 3.0);
            o[at + 1] = Clamp(-s * dx + c * dy, -3.0, 3.0);
            o[at + 2] = 1.0;
        }
    }
}
