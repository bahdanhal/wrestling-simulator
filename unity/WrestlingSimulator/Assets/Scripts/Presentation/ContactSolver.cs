using UnityEngine;

namespace Wrestling.Presentation
{
    /// <summary>
    /// What the mat can do for a body: given the force and turning moment a wrestler wants from the
    /// ground and the points where they touch it, finds contact forces that come as close as possible,
    /// each one inside its friction cone (a contact pushes, never pulls, and slides no harder than
    /// friction allows on what presses there). So a body can only lean, turn or push off the way its
    /// base allows: a turn that needs a pull is not made, a centre of pressure cannot leave the feet,
    /// and a push sideways needs weight on the mat. Display only.
    ///
    /// Each cone is a four-sided pyramid; contact forces are non-negative mixes of its edges, solved as
    /// a non-negative least-squares problem by projected Gauss-Seidel. Weight already resting on a
    /// contact (pressed there by gravity, not by the wrestler) counts towards its friction and may be
    /// shifted between contacts, so a resting body can still slide and roll itself.
    /// </summary>
    public sealed class ContactSolver
    {
        /// <summary>Most contact points considered (extra ones are ignored).</summary>
        public const int MaxContacts = 12;

        private const int EdgesPerContact = 4;
        private const int MaxColumns = MaxContacts * EdgesPerContact;
        private const int Iterations = 24;

        /// <summary>
        /// How much a newton-metre of turning error counts against a newton of force error (1/m). Below
        /// one: the force (holding the body up, pushing) comes first, the turn with what is left.
        /// </summary>
        private const float TorqueWeight = 0.6f;

        private const float Regularisation = 1e-4f;

        private readonly Vector3[] _points = new Vector3[MaxContacts];
        private readonly float[] _resting = new float[MaxContacts];
        private readonly Vector3[] _forces = new Vector3[MaxContacts];
        private readonly float[,] _basis = new float[6, MaxColumns];
        private readonly Vector3[] _edges = new Vector3[MaxColumns];
        private readonly float[,] _h = new float[MaxColumns, MaxColumns];
        private readonly float[] _g = new float[MaxColumns];
        private readonly float[] _lambda = new float[MaxColumns];
        private readonly float[] _target = new float[6];
        private int _count;

        public int Count => _count;

        public void Clear() => _count = 0;

        /// <summary>Adds a contact point with the weight (N) already resting on it.</summary>
        public void Add(Vector3 point, float resting)
        {
            if (_count >= MaxContacts)
            {
                return;
            }

            _points[_count] = point;
            _resting[_count] = Mathf.Max(0f, resting);
            _count++;
        }

        public Vector3 Point(int index) => _points[index];

        /// <summary>The force (N) the wrestler adds at contact <paramref name="index"/>, beyond the weight resting there.</summary>
        public Vector3 Force(int index) => _forces[index];

        /// <summary>
        /// Solves for contact forces that best make <paramref name="force"/> and <paramref name="torque"/>
        /// (about <paramref name="centre"/>), on top of the resting weight. Returns the force and torque
        /// they actually make.
        /// </summary>
        public (Vector3 Force, Vector3 Torque) Solve(Vector3 force, Vector3 torque, Vector3 centre, float friction)
        {
            if (_count == 0)
            {
                return (Vector3.zero, Vector3.zero);
            }

            // Target total ground force, resting weight included (the solved forces carry it).
            Vector3 totalForce = force;
            Vector3 totalTorque = torque;
            for (int i = 0; i < _count; i++)
            {
                var rest = new Vector3(0f, _resting[i], 0f);
                totalForce += rest;
                totalTorque += Vector3.Cross(_points[i] - centre, rest);
            }

            int columns = _count * EdgesPerContact;
            for (int i = 0; i < _count; i++)
            {
                Vector3 r = _points[i] - centre;
                for (int e = 0; e < EdgesPerContact; e++)
                {
                    int j = (i * EdgesPerContact) + e;
                    Vector3 edge = e switch
                    {
                        0 => new Vector3(friction, 1f, 0f),
                        1 => new Vector3(-friction, 1f, 0f),
                        2 => new Vector3(0f, 1f, friction),
                        _ => new Vector3(0f, 1f, -friction),
                    };
                    Vector3 moment = Vector3.Cross(r, edge) * TorqueWeight;
                    _edges[j] = edge;
                    _basis[0, j] = edge.x;
                    _basis[1, j] = edge.y;
                    _basis[2, j] = edge.z;
                    _basis[3, j] = moment.x;
                    _basis[4, j] = moment.y;
                    _basis[5, j] = moment.z;
                    _lambda[j] = 0f;
                }
            }

            float[] target = _target;
            target[0] = totalForce.x;
            target[1] = totalForce.y;
            target[2] = totalForce.z;
            target[3] = totalTorque.x * TorqueWeight;
            target[4] = totalTorque.y * TorqueWeight;
            target[5] = totalTorque.z * TorqueWeight;
            float trace = 0f;
            for (int a = 0; a < columns; a++)
            {
                float g = 0f;
                for (int k = 0; k < 6; k++)
                {
                    g += _basis[k, a] * target[k];
                }

                _g[a] = g;
                for (int b = a; b < columns; b++)
                {
                    float h = 0f;
                    for (int k = 0; k < 6; k++)
                    {
                        h += _basis[k, a] * _basis[k, b];
                    }

                    _h[a, b] = h;
                    _h[b, a] = h;
                }

                trace += _h[a, a];
            }

            float epsilon = Regularisation * trace / columns;
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                for (int a = 0; a < columns; a++)
                {
                    float residual = _g[a];
                    for (int b = 0; b < columns; b++)
                    {
                        residual -= _h[a, b] * _lambda[b];
                    }

                    residual -= epsilon * _lambda[a];
                    _lambda[a] = Mathf.Max(0f, _lambda[a] + (residual / (_h[a, a] + epsilon)));
                }
            }

            Vector3 madeForce = Vector3.zero;
            Vector3 madeTorque = Vector3.zero;
            for (int i = 0; i < _count; i++)
            {
                Vector3 f = Vector3.zero;
                for (int e = 0; e < EdgesPerContact; e++)
                {
                    int j = (i * EdgesPerContact) + e;
                    f += _edges[j] * _lambda[j];
                }

                f.y -= _resting[i];
                _forces[i] = f;
                madeForce += f;
                madeTorque += Vector3.Cross(_points[i] - centre, f);
            }

            return (madeForce, madeTorque);
        }
    }
}
