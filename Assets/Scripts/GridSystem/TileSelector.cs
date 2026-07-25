using UnityEngine;

public class TileSelector : MonoBehaviour
{
    SpriteRenderer renderer;


    void Start()
    {
        renderer = GetComponent<SpriteRenderer>();
    }

    void OnMouseEnter()
    {
        renderer.color = new Color(1, 1, 0, .5f);
    }

    void OnMouseExit()
    {
        renderer.color = new Color(1, 1, 1, .10f);
    }

    void OnMouseDown()
    {
        Debug.Log("Tile Selected");
    }
}
