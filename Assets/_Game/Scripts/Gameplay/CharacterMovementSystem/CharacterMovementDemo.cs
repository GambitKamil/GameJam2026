using System;
using UnityEngine;

namespace GameJam.CharacterMovement
{
    [RequireComponent(typeof(Rigidbody), typeof(CharacterMotionController))]
    public sealed class CharacterMovementDemo : MonoBehaviour
    {
        [Serializable]
        public sealed class ContactStation
        {
            public Collider Surface;
            public Transform LeftPalm, RightPalm;
            public bool Push;
        }

        [SerializeField] private Camera view;
        [SerializeField] private ContactStation[] stations;
        private Rigidbody body;
        private CharacterMotionController motion;
        private Vector3 wish, cameraVelocity;
        private ContactStation selected;
        private bool run, jump;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            motion = GetComponent<CharacterMotionController>();
        }

        private void Update()
        {
            wish = Vector3.ClampMagnitude(new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")), 1f);
            run = Input.GetKey(KeyCode.LeftShift);
            jump |= Input.GetKeyDown(KeyCode.Space);
            selected = null;
            if (Input.GetKey(KeyCode.E))
            {
                float closest = 0.94f * 0.94f;
                foreach (ContactStation station in stations)
                {
                    Vector3 gap = Vector3.ProjectOnPlane(station.RightPalm.position - transform.position, Vector3.up);
                    if (gap.sqrMagnitude >= closest)
                        continue;
                    closest = gap.sqrMagnitude;
                    selected = station;
                }
            }

            if (selected == null)
            {
                motion.ClearHandContact(HandSide.Left);
                motion.ClearHandContact(HandSide.Right);
                return;
            }

            SetHand(HandSide.Left, selected.LeftPalm);
            SetHand(HandSide.Right, selected.RightPalm);
        }

        private void SetHand(HandSide side, Transform palm)
        {
            if (palm == null)
            {
                motion.ClearHandContact(side);
                return;
            }
            motion.SetHandContact(side, new HandContact(palm.position, palm.rotation, selected.Surface,
                grip: selected.Push ? 0f : 1f, push: selected.Push ? 1f : 0f));
        }

        private void FixedUpdate()
        {
            float speed = selected != null ? 0.8f : run ? 2.85f : 1.55f;
            Vector3 velocity = body.linearVelocity;
            Vector3 planar = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), wish * speed, 12f * Time.fixedDeltaTime);
            float vertical = velocity.y;
            if (jump && Physics.Raycast(transform.position + Vector3.up * 0.12f, Vector3.down,
                0.18f, ~0, QueryTriggerInteraction.Ignore))
                vertical = 3.25f;
            jump = false;
            body.linearVelocity = new Vector3(planar.x, vertical, planar.z);

            Vector3 facing = selected != null ? selected.Surface.bounds.center - body.position : wish;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.01f)
                body.MoveRotation(Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(facing), 560f * Time.fixedDeltaTime));
        }

        private void LateUpdate()
        {
            if (view == null)
                return;
            Vector3 focus = transform.position + Vector3.up * 0.90f;
            view.transform.position = Vector3.SmoothDamp(view.transform.position,
                focus + new Vector3(3.0f, 1.85f, -4.3f), ref cameraVelocity, 0.14f);
            view.transform.rotation = Quaternion.LookRotation(focus - view.transform.position);
        }

        private void OnGUI()
        {
            const float width = 430f;
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height - 48f, width, 28f),
                "WASD - Move    Shift - Run    Space - Jump    Hold E - Interact");
        }
    }
}
