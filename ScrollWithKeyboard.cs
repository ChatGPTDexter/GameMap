using UnityEngine;
using UnityEngine.UI;

public class ScrollWithKeyboard : MonoBehaviour
{
    public ScrollRect scrollRect;
    public Scrollbar verticalScrollbar;
    public float scrollSpeed = 0.1f;

    void Start()
    {
        // Force the scrollbar to be visible
        if (verticalScrollbar != null)
        {
            verticalScrollbar.size = 0.9f;
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    void Update()
    {
        // Check if the up arrow key is pressed
        if (Input.GetKey(KeyCode.UpArrow))
        {
            ScrollUp();
        }

        // Check if the down arrow key is pressed
        if (Input.GetKey(KeyCode.DownArrow))
        {
            ScrollDown();
        }
    }

    // Method to scroll up
    void ScrollUp()
    {
        // Increase the verticalNormalizedPosition to move the content up
        float newScrollPosition = Mathf.Clamp(scrollRect.verticalNormalizedPosition + scrollSpeed * Time.deltaTime, 0f, 1f);
        scrollRect.verticalNormalizedPosition = newScrollPosition;
    }

    // Method to scroll down
    void ScrollDown()
    {
        // Decrease the verticalNormalizedPosition to move the content down
        float newScrollPosition = Mathf.Clamp(scrollRect.verticalNormalizedPosition - scrollSpeed * Time.deltaTime, 0f, 1f);
        scrollRect.verticalNormalizedPosition = newScrollPosition;
    }
}
