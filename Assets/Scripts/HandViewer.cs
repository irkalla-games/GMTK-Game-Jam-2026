using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;
using DG.Tweening;


public class HandViewer : MonoBehaviour
{
    [SerializeField] private SplineContainer splineContainer;

    [SerializeField] private float selectRaise = 0.75f;

    private readonly List<CardViewer> cardsInHand = new();

    public bool Contains(CardViewer cardViewer) => cardsInHand.Contains(cardViewer);

    public IEnumerator AddCard(CardViewer cardViewer)
    {
        cardsInHand.Add(cardViewer);
        yield return UpdateCardPosition(0.15f);
    }

    /// Empties the hand and destroys the viewers. The Card objects behind them are owned by the
    /// character, so nothing is lost - switching characters just rebuilds the row from the new hand.
    public void ClearHand()
    {
        foreach (CardViewer cardViewer in cardsInHand)
        {
            if (cardViewer != null) { Destroy(cardViewer.gameObject); }
        }

        cardsInHand.Clear();
    }

    public IEnumerator RemoveCard(CardViewer cardViewer)
    {
        cardsInHand.Remove(cardViewer);
        yield return UpdateCardPosition(0.15f);
    }

    /// Re-runs the layout without changing the hand - used when a card's selected state changes.
    public IEnumerator Relayout()
    {
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
            // The raise has to be applied here rather than tweened separately, or any AddCard/RemoveCard
            // during a selection would pull the selected card back down.
            Vector3 selectOffset = cardsInHand[i].isSelected ? Vector3.up * selectRaise : Vector3.zero;
            cardsInHand[i].transform.DOMove(splinePosition + transform.position + .01f * i * Vector3.back + selectOffset, duration);
            cardsInHand[i].transform.DORotate(rotation.eulerAngles, duration);
        }
        yield return new WaitForSeconds(duration);
    }
}
