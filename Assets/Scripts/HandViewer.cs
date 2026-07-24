using NUnit.Framework;
using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;
using DG.Tweening;
using Unity.VisualScripting;


public class HandViewer : MonoBehaviour
{
    [SerializeField] private SplineContainer splineContainer;

    private readonly List<CardViewer> cardsInHand = new();

    public IEnumerator AddCard(CardViewer cardViewer)
    {
        cardsInHand.Add(cardViewer);
        yield return UpdateCardPosition(0.15f);
    }

    private IEnumerator UpdateCardPosition(float duration)
    {
        if (cardsInHand.Count == 0) { yield break; }

        float cardSpacing = 1f / 10f;
        float firstPosition = 0.5f - (cardsInHand.Count - 1) * cardSpacing / 2;
        Spline spline = splineContainer.Spline;
        for (int i = 0; i < cardsInHand.Count; i++)
        {
            float p = firstPosition + i * cardSpacing;
            Vector3 splinePosition = spline.EvaluatePosition(p);
            Vector3 forward = spline.EvaluateTangent(p);
            Vector3 up = spline.EvaluateUpVector(p);
            Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(forward, up).normalized, up);
            cardsInHand[i].transform.DOMove(splinePosition + transform.position + .01f * i * Vector3.back, duration);
            cardsInHand[i].transform.DORotate(rotation.eulerAngles, duration);
        }
        yield return new WaitForSeconds(duration);
    }
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
