using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TarodevController {
    /// <summary>
    /// This is a pretty filthy script. I was just arbitrarily adding to it as I went.
    /// You won't find any programming prowess here.
    /// This is a supplementary script to help with effects and animation. Basically a juice factory.
    /// </summary>
    public class PlayerAnimator : MonoBehaviour {
        [SerializeField] private Animator _anim;
        [SerializeField] private AudioSource _source;
        [SerializeField] private LayerMask _groundMask;
        [SerializeField] private ParticleSystem _jumpParticles, _launchParticles;
        [SerializeField] private ParticleSystem _moveParticles, _landParticles;
        [SerializeField] private AudioClip[] _footsteps;
        [SerializeField] private float _maxTilt = .1f;
        [SerializeField] private float _tiltSpeed = 1;
        [SerializeField, Range(1f, 3f)] private float _maxIdleSpeed = 2;
        [SerializeField] private float _maxParticleFallSpeed = -40;
        [SerializeField] int  currentState;
        [SerializeField] bool landState;
        [SerializeField, Range(0f, 1f) ] private float _landingAnimTime = 0.2f;
        [SerializeField] private float _parryAnimTime = 0.2f;
        float _lockedTill;

        private PlayerController _playerController;
        private IPlayerController _player;
        private bool _playerGrounded;
        private ParticleSystem.MinMaxGradient _currentGradient;
        private Vector2 _movement;

        void Awake() => _player = GetComponentInParent<IPlayerController>();


        void Update() {
            if (_player == null) return;

            //Animator
            var state = GetState();

            if (state != currentState)
            {
                Debug.Log("Animator playing!");
                _anim.CrossFade(state, 0, 0);
                currentState = state;
            }

            // Flip the sprite
            if (_player.Input.X != 0)
            {
                transform.localScale = new Vector3(_player.Input.X > 0 ? 2f : -2f, 2f, 1); ;
            }

            // Lean while running
            var targetRotVector = new Vector3(0, 0, Mathf.Lerp(-_maxTilt, _maxTilt, Mathf.InverseLerp(-1, 1, _player.Input.X)));
            _anim.transform.rotation = Quaternion.RotateTowards(_anim.transform.rotation, Quaternion.Euler(targetRotVector), _tiltSpeed * Time.deltaTime);


            // Speed up idle while running
            // _anim.SetFloat(Idle, Mathf.Lerp(1, _maxIdleSpeed, Mathf.Abs(_player.Input.X)));

            // Splat
            if (_player.LandingThisFrame)
            {
                _source.PlayOneShot(_footsteps[Random.Range(0, _footsteps.Length)]);
                landState = true;
            }

            // Jump effects
            if (_player.JumpingThisFrame) {

                // Only play particles when grounded (avoid coyote)
                if (_player.Grounded) {
                    SetColor(_jumpParticles);
                    SetColor(_launchParticles);
                    _jumpParticles.Play();
                }
            }

            // Play landing effects and begin ground movement effects
            if (!_playerGrounded && _player.Grounded) {
                _playerGrounded = true;
                _moveParticles.Play();
                _landParticles.transform.localScale = Vector3.one * Mathf.InverseLerp(0, _maxParticleFallSpeed, _movement.y);
                SetColor(_landParticles);
                _landParticles.Play();
                
            }
            else if (_playerGrounded && !_player.Grounded) {
                _playerGrounded = false;
                _moveParticles.Stop();
            }

            // Detect ground color
            var groundHit = Physics2D.Raycast(transform.position, Vector3.down, 2, _groundMask);
            if (groundHit && groundHit.transform.TryGetComponent(out SpriteRenderer r)) {
                _currentGradient = new ParticleSystem.MinMaxGradient(r.color * 0.9f, r.color * 1.2f);
                SetColor(_moveParticles);
            }
            _movement = _player.RawMovement; // Previous frame movement is more valuable
        }

        int GetState()
        {
            //Se alguma animaçao não-cancelável ainda tá tocando, não mudar
            if (Time.time < _lockedTill) return currentState;

            //Ordenado em ordem de prioridade
            //if (currentState == Parry)
            //{
            //    if (_player.ParryingThisFrame) return LockState(Parry, _parryAnimTime);
            //}
            if (landState && _player.Input.X == 0)
            {
                landState = false;
                return LockState(Land, _landingAnimTime); ;
            }
            else if (landState) landState = false;
            if (_playerGrounded) return _player.Input.X == 0? Idle : Walk;            

            return _player.Velocity.y > 0 ? Jump : Falling;

            int LockState(int s, float t)
            {
                _lockedTill = Time.time + t;
                return s;
            }
        }

        private void OnDisable() {
            _moveParticles.Stop();
        }

        private void OnEnable() {
            _moveParticles.Play();
        }

        public void OnAnimationChanged(int newAnim)
        {
            
            currentState = newAnim;
        }

        void SetColor(ParticleSystem ps) {
            var main = ps.main;
            main.startColor = _currentGradient;
        }

        #region Animation Keys

        private static readonly int Idle = Animator.StringToHash("JammaIdle");
        private static readonly int Jump = Animator.StringToHash("JammaJump");
        private static readonly int Falling = Animator.StringToHash("JammaFall");
        private static readonly int Walk = Animator.StringToHash("JammaWalk");
        private static readonly int Run = Animator.StringToHash("JammaRun");
        private static readonly int Land = Animator.StringToHash("JammaLand");
        private static readonly int Parry = Animator.StringToHash("JammaParry");
        private static readonly int Blocking = Animator.StringToHash("JammaBlock");
        private static readonly int Rolling = Animator.StringToHash("JammaRoll");

        #endregion
    }


}