using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BarScript : MonoBehaviour
{
    const float DAMAGE_TIMER_MAX = 0.5F;

    public Image frontFill;
    public Image backFill;
    private float health;
    private float damagedHealthTimer;
    bool shrinkState = false;

    void Start()
    {
        backFill.fillAmount = 1;
        health = 100f;

    }

    // Update is called once per frame
    void Update()
    {
        health = Mathf.Clamp(health, 0, 100f);
        if (Input.GetKeyDown(KeyCode.Y))
        {
            TakeDamage();
        }
        if (Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            HealDamage();
        }
        UpdateHealthBar();

        //Debug.Log(damagedHealthTimer);
    }

    void UpdateHealthBar()
    {
        float healthFraction = health / 100f;
        float fillB = backFill.fillAmount;
        damagedHealthTimer -= Time.deltaTime;
        frontFill.fillAmount = healthFraction;

        if(damagedHealthTimer < -2f) {
            damagedHealthTimer = 0f;
        }
        
        if (damagedHealthTimer < 0) {
            if (fillB > healthFraction) {
                shrinkState = true;
                backFill.fillAmount -= 0.5f * Time.deltaTime;
            }else shrinkState = false;
        }

        if (healthFraction > backFill.fillAmount)
        {
            backFill.fillAmount = healthFraction;
        }
    }

    void TakeDamage()
    {
        if(shrinkState == false) { 
        damagedHealthTimer = DAMAGE_TIMER_MAX;
        }
        health -= 10;
    }

    void HealDamage()
    {
        health += 10;
    }
}