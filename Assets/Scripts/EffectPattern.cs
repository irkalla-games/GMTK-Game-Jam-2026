using UnityEngine;

[CreateAssetMenu(menuName = "Card/Effect Pattern")]
public class EffectPattern : ScriptableObject
{
    [SerializeField]
    private int width = 3;

    [SerializeField]
    private int height = 3;

    [SerializeField]
    private bool[] cells;


    private void OnEnable()
    {
        InitializeCells();
    }


    private void InitializeCells()
    {
        int size = width * height;

        if (cells == null || cells.Length != size)
        {
            cells = new bool[size];
        }
    }


    public bool GetCell(int x, int y)
    {
        InitializeCells();

        int index = y * width + x;

        return cells[index];
    }


    public void SetCell(int x, int y, bool value)
    {
        InitializeCells();

        int index = y * width + x;

        cells[index] = value;
    }


    public int Width => width;
    public int Height => height;
}