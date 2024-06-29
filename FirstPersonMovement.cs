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

    new Rigidbody rigidbody;
    public List<System.Func<float>> speedOverrides = new List<System.Func<float>>();

    private bool isMovementEnabled = true;
    private CharacterAI activeCharacter;
    private SpawnCharacterAI SactiveCharacter;
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
        else if (SactiveCharacter != null && SactiveCharacter.userInputField != null && SactiveCharacter.userInputField.isFocused)
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
        else if (SactiveCharacter != null && SactiveCharacter.userInputField != null && SactiveCharacter.userInputField.isFocused)
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

        // Use GetAxisRaw to differentiate between WASD and arrow keys
        float horizontalInput = Input.GetAxisRaw("Horizontal");
        float verticalInput = Input.GetAxisRaw("Vertical");

        // Check if the inputs are coming from arrow keys and ignore them
        if (IsUsingArrowKeys())
        {
            horizontalInput = 0;
            verticalInput = 0;
        }

        Vector2 targetVelocity = new Vector2(horizontalInput * targetMovingSpeed, verticalInput * targetMovingSpeed);

        rigidbody.velocity = transform.rotation * new Vector3(targetVelocity.x, rigidbody.velocity.y, targetVelocity.y);
    }

    private bool IsUsingArrowKeys()
    {
        // Check if the input is from arrow keys
        return Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
    }

    private void InteractWithCharacter()
    {
        var closestCharacter = characterSpawner.GetClosestCharacter(transform.position, interactionRadius);

        if (closestCharacter != null)
        {
            if (closestCharacter is CharacterAI characterAI)
            {
                activeCharacter = characterAI;  // Cast to CharacterAI
                activeCharacter.EnableInteraction();
                DisableMovement();
            }
            else if (closestCharacter is SpawnCharacterAI spawnCharacterAI)
            {
                SactiveCharacter = spawnCharacterAI;  // Cast to SpawnCharacterAI
                SactiveCharacter.EnableInteraction();
                DisableMovement();
            }
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
        else if (SactiveCharacter != null)
        { 
        SactiveCharacter.DisableInteraction();
        SactiveCharacter = null;
        }
    }

    public void DisableMovement()
    {
        isMovementEnabled = false;
        Cursor.lockState = CursorLockMode.None;
    }
}
