using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class FirstPersonMovement : MonoBehaviour
{
    public float speed = 5;
    public bool canRun = true;
    public bool IsRunning { get; private set; }
    public float runSpeed = 9;
    public KeyCode runningKey = KeyCode.LeftShift;
    public float interactionRadius = 10f;

    private Rigidbody rigidbody;
    public List<System.Func<float>> speedOverrides = new List<System.Func<float>>();

    private bool isMovementEnabled = true;
    private CharacterAI activeCharacter;
    private CharacterSpawner characterSpawner;

    void Awake()
    {
        rigidbody = GetComponent<Rigidbody>();
        characterSpawner = FindObjectOfType<CharacterSpawner>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (EventSystem.current.IsPointerOverGameObject() || !isMovementEnabled)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        // Skip other actions if the input field is focused
        if (activeCharacter != null && activeCharacter.userInputField != null && activeCharacter.userInputField.isFocused)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Q))
        {
            InteractWithCharacter();
        }
    }

    void FixedUpdate()
    {
        // Skip other actions if the input field is focused
        if (activeCharacter != null && activeCharacter.userInputField != null && activeCharacter.userInputField.isFocused)
        {
            return;
        }

        if (!isMovementEnabled || EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        IsRunning = canRun && Input.GetKey(runningKey);

        float targetMovingSpeed = IsRunning ? runSpeed : speed;
        if (speedOverrides.Count > 0)
        {
            targetMovingSpeed = speedOverrides[speedOverrides.Count - 1]();
        }

        Vector2 targetVelocity = new Vector2(Input.GetAxis("Horizontal") * targetMovingSpeed, Input.GetAxis("Vertical") * targetMovingSpeed);

        rigidbody.velocity = transform.rotation * new Vector3(targetVelocity.x, rigidbody.velocity.y, targetVelocity.y);
    }

    private void InteractWithCharacter()
    {
        CharacterAI closestCharacter = characterSpawner.GetClosestCharacter(transform.position, interactionRadius);
        if (closestCharacter != null)
        {
            activeCharacter = closestCharacter;
            activeCharacter.EnableInteraction();
            DisableMovement();
        }
    }

    public void EnableMovement()
    {
        isMovementEnabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        if (activeCharacter != null)
        {
            activeCharacter.DisableInteraction();
            activeCharacter = null;
        }
    }

    public void DisableMovement()
    {
        isMovementEnabled = false;
        Cursor.lockState = CursorLockMode.None;
    }
}
