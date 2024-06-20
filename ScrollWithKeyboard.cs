using UnityEngine;
using UnityEngine.UI;

public class ScrollWithKeyboard : MonoBehaviour
{
    public ScrollRect scrollRect; // Reference to the ScrollRect component
    public float scrollSpeed = 0.1f; // Adjust the scrolling speed

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
