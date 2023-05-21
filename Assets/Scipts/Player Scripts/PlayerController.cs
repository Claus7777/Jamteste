using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarodevController {
    /// <summary>
    /// Hey!
    /// Tarodev here. I built this controller as there was a severe lack of quality & free 2D controllers out there.
    /// Right now it only contains movement and jumping, but it should be pretty easy to expand... I may even do it myself
    /// if there's enough interest. You can play and compete for best times here: https://tarodev.itch.io/
    /// If you hve any questions or would like to brag about your score, come to discord: https://discord.gg/GqeHHnhHpz
    /// </summary>
    public class PlayerController : MonoBehaviour, IPlayerController {
        // Public for external hooks
        public Vector3 Velocity { get; private set; }
        public FrameInput Input { get; private set; }
        public bool JumpingThisFrame { get; private set; }
        public bool LandingThisFrame { get; private set; }
        public bool ParryingThisFrame { get; private set; }
        public bool BlockingThisFrame { get; private set; }
        public bool RollingThisFrame { get; private set; }
        public Vector3 RawMovement { get; private set; }

        public bool Grounded => _colDown;
        

        public delegate void AnimationChanger(int animHash);
        public event AnimationChanger animationChanged;

        //Coisa de state
        [SerializeField] private enum State
        {
            Blocking,
            Parrying,
            Rolling,
            Attacking,
            Normal
        }
        [SerializeField] private State state;
        private State lastState;


        //posicoes e velocidade
        private Vector3 _lastPosition;
        private float _currentHorizontalSpeed, _currentVerticalSpeed;
        private float _parryStart, _parryEnd;

        // This is horrible, but for some reason colliders are not fully established when update starts...
        private bool _active;
        void Awake() => Invoke(nameof(Activate), 0.5f);
        void Activate() =>  _active = true;
        
        private void Update() {
            if(!_active) return;
            // Calculate velocity
            Velocity = (transform.position - _lastPosition) / Time.deltaTime;
            _lastPosition = transform.position;

            GatherInput();
            RunCollisionChecks();

            lastState = state;
            if (_rollingCD != 0)
            {
                if (Time.time >= _rollingCD)
                {
                    _rollingCD = 0;
                }
            }

            //State Machine
            switch (state){
                case State.Normal:
                    CheckforSkillState();
                    CalculateWalk(); // Horizontal movement
                    CalculateJumpApex(); // Affects fall speed, so calculate before gravity
                    CalculateGravity(); // Vertical movement
                    CalculateJump(); // Possibly overrides vertical


                    MoveCharacter(); // Actually perform the axis movement
                    break;

                case State.Parrying:
                    if (ParryingThisFrame == false)
                    {
                        _parryEnd = Time.time + _parryLength;
                        _parryStart = Time.time;
                        JammaParry(_parryStart, _parryEnd); //Manda fazer parry e a frame que o parry começou
                        break;
                    }
                    _parryStart += Time.deltaTime;
                    JammaParry(_parryStart, _parryEnd);
                    break;

                case State.Blocking:
                    JammaBlock();
                    break;
                    

                case State.Rolling:

                    if (RollingThisFrame == false)
                    {
                        _currentHorizontalSpeed = 0;
                        _rollEnding = Time.time + rollLength;
                        _rollStartFrame = Time.time;
                        rollingX = Input.X;
                        CalculateRoll(_rollStartFrame, _rollEnding, rollingX) ;
                        break;
                    }
                    else
                        _rollStartFrame += Time.deltaTime;
                        CalculateRoll(_rollStartFrame, _rollEnding, rollingX);
                    break;

                case State.Attacking:
                    SummonFire();
                    break;
            }

            Debug.Log(state);
            if (ParryingThisFrame) Debug.Log("Parrying this frame");
        }

        private void CheckforSkillState()
        {
            for (int i = 0; i < 59; i++) //checar o buffer pra ver se o botão de block foi solto alguma hora nas ultimas 60 frames
            {
                {
                    if (!inputBuffer[(Time.frameCount - i) % bufferSize].Block)
                    {
                        if (Input.Block && Grounded && _rollingCD == 0)
                        {
                            state = State.Parrying;
                        }
                    }
                }
            }
        }

        #region Buffer

        static int bufferSize = 60;
        [SerializeField] FrameInput[] inputBuffer = new FrameInput[bufferSize];

        #endregion

        #region Gather Input

        private void GatherInput() {
            Input = new FrameInput {
                JumpDown = UnityEngine.Input.GetButtonDown("Jump"),
                JumpUp = UnityEngine.Input.GetButtonUp("Jump"),
                X = UnityEngine.Input.GetAxisRaw("Horizontal"),
                Block = UnityEngine.Input.GetButton("Fire1")
            };
            if (Input.JumpDown) {
                _lastJumpPressed = Time.time;
            }
            inputBuffer[(Time.frameCount % bufferSize)] = Input;
        }

        #endregion

        #region Collisions

        [Header("COLLISION")] [SerializeField] private Bounds _characterBounds;
        [SerializeField] private LayerMask _groundLayer;
        [SerializeField] private int _detectorCount = 3;
        [SerializeField] private float _detectionRayLength = 0.1f;
        [SerializeField] [Range(0.1f, 0.3f)] private float _rayBuffer = 0.1f; // Prevents side detectors hitting the ground

        private RayRange _raysUp, _raysRight, _raysDown, _raysLeft;
        private bool _colUp, _colRight, _colDown, _colLeft;

        private float _timeLeftGrounded;

        // We use these raycast checks for pre-collision information
        private void RunCollisionChecks() {
            // Generate ray ranges. 
            CalculateRayRanged();

            // Ground
            LandingThisFrame = false;
            var groundedCheck = RunDetection(_raysDown);
            if (_colDown && !groundedCheck) _timeLeftGrounded = Time.time; // Only trigger when first leaving
            else if (!_colDown && groundedCheck) {
                _coyoteUsable = true; // Only trigger when first touching
                LandingThisFrame = true;
            }

            _colDown = groundedCheck;

            // The rest
            _colUp = RunDetection(_raysUp);
            _colLeft = RunDetection(_raysLeft);
            _colRight = RunDetection(_raysRight);

            bool RunDetection(RayRange range) {
                return EvaluateRayPositions(range).Any(point => Physics2D.Raycast(point, range.Dir, _detectionRayLength, _groundLayer));
            }
        }

        private void CalculateRayRanged() {
            // This is crying out for some kind of refactor. 
            var b = new Bounds(transform.position, _characterBounds.size);

            _raysDown = new RayRange(b.min.x + _rayBuffer, b.min.y, b.max.x - _rayBuffer, b.min.y, Vector2.down);
            _raysUp = new RayRange(b.min.x + _rayBuffer, b.max.y, b.max.x - _rayBuffer, b.max.y, Vector2.up);
            _raysLeft = new RayRange(b.min.x, b.min.y + _rayBuffer, b.min.x, b.max.y - _rayBuffer, Vector2.left);
            _raysRight = new RayRange(b.max.x, b.min.y + _rayBuffer, b.max.x, b.max.y - _rayBuffer, Vector2.right);
        }


        private IEnumerable<Vector2> EvaluateRayPositions(RayRange range) {
            for (var i = 0; i < _detectorCount; i++) {
                var t = (float)i / (_detectorCount - 1);
                yield return Vector2.Lerp(range.Start, range.End, t);
            }
        }

        private void OnDrawGizmos() {
            // Bounds
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + _characterBounds.center, _characterBounds.size);

            // Rays
            if (!Application.isPlaying) {
                CalculateRayRanged();
                Gizmos.color = Color.blue;
                foreach (var range in new List<RayRange> { _raysUp, _raysRight, _raysDown, _raysLeft }) {
                    foreach (var point in EvaluateRayPositions(range)) {
                        Gizmos.DrawRay(point, range.Dir * _detectionRayLength);
                    }
                }
            }

            if (!Application.isPlaying) return;

            // Draw the future position. Handy for visualizing gravity
            Gizmos.color = Color.red;
            var move = new Vector3(_currentHorizontalSpeed, _currentVerticalSpeed) * Time.deltaTime;
            Gizmos.DrawWireCube(transform.position + move, _characterBounds.size);
        }

        #endregion

        #region Abilities

        [Header("HABILIDADES")]
        [SerializeField] private float _parryLength;

        
        [SerializeField] float rollLength;
        [SerializeField] private AnimationCurve rollCurve;
        [SerializeField] private float rollingCooldown;
        private float _rollEnding;
        private float _rollStartFrame;
        private float rollingX;
        [SerializeField] private float _rollingCD;

        private void JammaParry(float parryStart, float parryEnd)
        {  
            //Checar o buffer pra ver se o player quis rolar ao invés de parry vendo se ouve mudança de 0 para não-zero juntamente com apertar block em um intervalo de X frames
            bool possibleRoll = false;
            for (int i = 0; i < 10; i++) //o i checa quantas frame a função volta pra ver se pode mudar pra roll ou não
            {
                ParryingThisFrame = false;

                if (inputBuffer[(Time.frameCount - i) % bufferSize].X != 0) //ve as ultimas i frames, se ouve mudança de neutro para alguma direção, rolar
                {
                    possibleRoll = true;
                }

                if (possibleRoll)
                {
                    if (inputBuffer[(Time.frameCount - i) % bufferSize].X == 0 || inputBuffer[(Time.frameCount - i) % bufferSize].X != Input.X)
                    {
                        state = State.Rolling;
                        return;
                    }
                }
            }

            if (parryStart < parryEnd)
            {
                ParryingThisFrame = true;;
            }
            else { 
            ParryingThisFrame = false;
                if (Input.Block && Grounded) state = State.Blocking;
                else state = State.Normal;
            }

        }

        private void JammaBlock() {
            if (Input.Block && Input.X != 0)
            {
                for (int i = 0; i < 60; i++) //ver o buffer pra ver se o jogador só não ficou segurando pra frente
                {
                    if (inputBuffer[(Time.frameCount - i) % bufferSize].X == 0) //ve as ultimas i frames, se ouve o jogador soltou a direção em algum momento, rolar
                    {
                        BlockingThisFrame = false;
                        state = State.Rolling;
                        return;
                    }
                }   
            }
            if (Input.Block)
            {
                BlockingThisFrame = true;
            }
            else
            {
                BlockingThisFrame = false;
                state = State.Normal;
            }
        }

        private void CalculateRoll(float rollStart, float rollEnd, float rollingDirection)
        {
            _coyoteUsable = true;
            if (rollStart < rollEnd && _rollingCD == 0)//Se a frame atual ainda nao chegou na frame onde o roll acaba E o roll não estiver em cooldown, rolar
            {
                RollingThisFrame = true;
                _currentHorizontalSpeed += rollingDirection * (rollCurve.Evaluate(rollStart) * 0.2f);
                MoveCharacter();
                CalculateGravity();
                CalculateJump(); 
            }
            else
            { //parou de rolar, ativa o cooldown e voltamos pro estado normal
                _rollingCD = Time.time + rollingCooldown;
                RollingThisFrame = false;
                CalculateWalk();
                state = State.Normal;
            }
        }

        private void SummonFire() {

        }

        #endregion

        #region Walk

        [Header("WALKING")] [SerializeField] private float _acceleration = 90;
        [SerializeField] private float _moveClamp = 13;
        [SerializeField] private float _deAcceleration = 60f;
        [SerializeField] private float _apexBonus = 2;

        private void CalculateWalk() {
            if (Input.X != 0) {
                // Set horizontal move speed
                _currentHorizontalSpeed += Input.X * _acceleration * Time.deltaTime;

                // clamped by max frame movement
                if(!RollingThisFrame) _currentHorizontalSpeed = Mathf.Clamp(_currentHorizontalSpeed, -_moveClamp, _moveClamp);


                // Apply bonus at the apex of a jump
                var apexBonus = Mathf.Sign(Input.X) * _apexBonus * _apexPoint;
                _currentHorizontalSpeed += apexBonus * Time.deltaTime;
            }
            else {
                // No input. Let's slow the character down
                _currentHorizontalSpeed = Mathf.MoveTowards(_currentHorizontalSpeed, 0, _deAcceleration * Time.deltaTime);
            }

            if (_currentHorizontalSpeed > 0 && _colRight || _currentHorizontalSpeed < 0 && _colLeft) {
                // Don't walk through walls
                _currentHorizontalSpeed = 0;
            }
        }

        #endregion

        #region Gravity

        [Header("GRAVITY")] [SerializeField] private float _fallClamp = -40f;
        [SerializeField] private float _minFallSpeed = 80f;
        [SerializeField] private float _maxFallSpeed = 120f;
        private float _fallSpeed;

        private void CalculateGravity() {
            if (_colDown) {
                // Move out of the ground
                if (_currentVerticalSpeed < 0) _currentVerticalSpeed = 0;
            }
            else {
                // Add downward force while ascending if we ended the jump early
                var fallSpeed = _endedJumpEarly && _currentVerticalSpeed > 0 ? _fallSpeed * _jumpEndEarlyGravityModifier : _fallSpeed;

                // Fall
                _currentVerticalSpeed -= fallSpeed * Time.deltaTime;

                // Clamp
                if (_currentVerticalSpeed < _fallClamp) _currentVerticalSpeed = _fallClamp;
            }
        }

        #endregion

        #region Jump

        [Header("JUMPING")] [SerializeField] private float _jumpHeight = 30;
        [SerializeField] private float _jumpApexThreshold = 10f;
        [SerializeField] private float _coyoteTimeThreshold = 0.1f;
        [SerializeField] private float _jumpBuffer = 0.1f;
        [SerializeField] private float _jumpEndEarlyGravityModifier = 3;
        private bool _coyoteUsable;
        private bool _endedJumpEarly = true;
        private float _apexPoint; // Becomes 1 at the apex of a jump
        private float _lastJumpPressed;
        private bool CanUseCoyote => _coyoteUsable && !_colDown && _timeLeftGrounded + _coyoteTimeThreshold > Time.time;
        private bool HasBufferedJump => _colDown && _lastJumpPressed + _jumpBuffer > Time.time;

        private void CalculateJumpApex() {
            if (!_colDown) {
                // Gets stronger the closer to the top of the jump
                _apexPoint = Mathf.InverseLerp(_jumpApexThreshold, 0, Mathf.Abs(Velocity.y));
                _fallSpeed = Mathf.Lerp(_minFallSpeed, _maxFallSpeed, _apexPoint);
            }
            else {
                _apexPoint = 0;
            }
        }

        private void CalculateJump() {
            // Jump if: grounded or within coyote threshold || sufficient jump buffer
            if (Input.JumpDown && CanUseCoyote || HasBufferedJump) {
                _currentVerticalSpeed = _jumpHeight;
                _endedJumpEarly = false;
                _coyoteUsable = false;
                _timeLeftGrounded = float.MinValue;
                JumpingThisFrame = true;
            }
            else {
                JumpingThisFrame = false;
            }

            // End the jump early if button released
            if (!_colDown && Input.JumpUp && !_endedJumpEarly && Velocity.y > 0) {
                //_currentVerticalSpeed = 0;
                _endedJumpEarly = true;
            }

            if (_colUp) {
                if (_currentVerticalSpeed > 0) _currentVerticalSpeed = 0;
            }
        }

        #endregion

        #region Move

        [Header("MOVE")] [SerializeField, Tooltip("Raising this value increases collision accuracy at the cost of performance.")]
        private int _freeColliderIterations = 10;

        // We cast our bounds before moving to avoid future collisions
        private void MoveCharacter() {
            var pos = transform.position;
            RawMovement = new Vector3(_currentHorizontalSpeed, _currentVerticalSpeed); // Used externally
            var move = RawMovement * Time.deltaTime;
            var furthestPoint = pos + move;

            // check furthest movement. If nothing hit, move and don't do extra checks
            var hit = Physics2D.OverlapBox(furthestPoint, _characterBounds.size, 0, _groundLayer);
                        if (!hit) {
                transform.position += move;
                return;
            }

            // otherwise increment away from current pos; see what closest position we can move to
            var positionToMoveTo = transform.position;
            for (int i = 1; i < _freeColliderIterations; i++) {
                // increment to check all but furthestPoint - we did that already
                var t = (float)i / _freeColliderIterations;
                
                
                var posToTry = Vector2.Lerp(pos, furthestPoint, t);

                if (Physics2D.OverlapBox(posToTry, _characterBounds.size, 0, _groundLayer)) {
                    transform.position = positionToMoveTo;

                    // We've landed on a corner or hit our head on a ledge. Nudge the player gently
                    if (i == 1) {
                        if (_currentVerticalSpeed < 0) _currentVerticalSpeed = 0;
                        var dir = transform.position - hit.transform.position;
                        transform.position += dir.normalized * move.magnitude;
                    }

                    return;
                }

                positionToMoveTo = posToTry;
            }
        }

        #endregion
    }
}