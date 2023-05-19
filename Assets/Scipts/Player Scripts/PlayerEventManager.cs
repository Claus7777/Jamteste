using System.Collections;
using System.Collections.Generic;
using TarodevController;
using UnityEngine;

public class PlayerEventManager : MonoBehaviour
{
    PlayerController playerController;
    PlayerAnimator playerAnimator;

    // Start is called before the first frame update
    void Start()
    {
        playerController = FindObjectOfType<PlayerController>();
        playerAnimator = FindObjectOfType<PlayerAnimator>();

        playerController.animationChanged += playerAnimator.OnAnimationChanged;
    }
}
