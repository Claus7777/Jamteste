using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerStateManager : MonoBehaviour
{
    statePlayerBase currentState; //referencia ao estado atual da state machine
    statePlayerIdle IdleState = new statePlayerIdle();
    statePlayerWalk WalkState = new statePlayerWalk();
    statePlayerJump JumpState = new statePlayerJump();  


    // Start is called before the first frame update
    void Start()
    {
        currentState = IdleState;
        currentState.EnterState(this);
    }

    // Update is called once per frame
    void Update()
    {
        currentState.UpdateState(this);
    }
}
