using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class testscript : MonoBehaviour
{

    public ParticleSystem ps;
    void Start()
    {
        var main = ps.main;
        main.stopAction = ParticleSystemStopAction.Callback;
    }

    void OnParticleSystemStopped()
    {
        Debug.Log("System has stopped!");
    }
}
