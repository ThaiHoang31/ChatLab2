# PRN222 — Lab 1: Chat Client

A simple multi-client TCP chat application built with **C# / .NET 9**, consisting of a Console-based TCP Server and a WPF Chat Client.

The purpose of this lab is to practice fundamental networking concepts including:

* Client–Server architecture
* TCP communication
* `TcpListener`
* `TcpClient`
* `NetworkStream`
* Asynchronous network I/O
* Multiple client connections
* Message broadcasting
* Application-level message protocol
* TCP message framing
* WPF client interface

---

## 1. Project Overview

The application allows multiple clients to connect to a central TCP server and communicate in a shared chat room.

```text
                    TCP
                     │
                     ▼
            ┌─────────────────┐
            │   Chat Server   │
            │                 │
            │ TcpListener     │
            │ Client List     │
            │ Broadcast       │
            │ User Management │
            └────────┬────────┘
                     │
          ┌──────────┼──────────┐
          │          │          │
          ▼          ▼          ▼
       Client A   Client B   Client C
          │          │          │
          └──────────┼──────────┘
                     │
                 WPF Client
```

The server acts as the central coordinator. Clients connect to the server, send messages to it, and receive messages broadcast by the server.

---

# 2. Objectives

The main objectives of Lab 1 are:

1. Understand the basic Client–Server model.
2. Establish a TCP connection between client and server.
3. Send and receive data through `NetworkStream`.
4. Support multiple clients simultaneously.
5. Broadcast messages to connected clients.
6. Manage usernames and online users.
7. Build a simple WPF chat interface.
8. Understand why TCP requires application-level message framing.
9. Apply asynchronous programming to network communication.

---

# 3. Technologies

| Technology      | Purpose                    |
| --------------- | -------------------------- |
| C#              | Programming language       |
| .NET 9          | Application framework      |
| WPF             | Chat client UI             |
| TCP             | Transport protocol         |
| `TcpListener`   | Server-side TCP listener   |
| `TcpClient`     | Client-side TCP connection |
| `NetworkStream` | Read/write TCP data        |
| UTF-8           | Convert strings to bytes   |
| Visual Studio   | Development environment    |

---

# 4. Solution Structure

```text
ChatLab1
│
├── ChatServer
│   └── Program.cs
│
└── ChatClient
    │
    ├── MainWindow.xaml
    ├── MainWindow.xaml.cs
    │
    └── Controls
        ├── MessageBubble.xaml
        └── MessageBubble.xaml.cs
```

### ChatServer

Responsible for:

* Starting the TCP server
* Listening for incoming clients
* Accepting TCP connections
* Receiving usernames
* Managing connected clients
* Broadcasting messages
* Managing online users
* Handling client disconnects
* Message framing

### ChatClient

Responsible for:

* Connecting to the server
* Sending username
* Sending chat messages
* Receiving server messages
* Displaying chat messages
* Displaying online users
* Connect / Disconnect UI
* Emoji selection
* Enter-to-send
* Auto scrolling

---

# 5. Architecture

The application follows a basic Client–Server architecture.

```text
Client
   │
   │ TCP Connection
   ▼
Server
   │
   ├── Client A
   ├── Client B
   └── Client C
```

The server maintains the shared state of the chat room.

For example:

```text
Server
 ├── Alice
 ├── Bob
 └── Charlie
```

When Alice sends a message:

```text
Alice
  │
  │ "Hello"
  ▼
Server
  │
  ├────────► Alice
  ├────────► Bob
  └────────► Charlie
```

This is implemented using server-side broadcasting.

---

# 6. Why TCP?

The application uses TCP because a basic chat application benefits from:

* Reliable delivery
* Ordered byte transmission
* Connection-oriented communication
* Automatic retransmission when necessary

For example, the application expects:

```text
Message 1: Hello
Message 2: How are you?
Message 3: Good morning
```

to be received in the same order.

However, TCP itself does **not** understand application-level messages.

TCP provides a:

```text
Reliable Ordered Byte Stream
```

rather than:

```text
Message 1
Message 2
Message 3
```

This distinction is important in this project.

---

# 7. TCP Message Framing

One of the main networking problems encountered during development was TCP message boundaries.

Suppose the application sends:

```text
[NAME]Alice
[SERVER]Alice joined the chat.
[ONLINE]Alice|Bob
```

The receiver may not receive them as three separate `ReadAsync()` results.

It may receive:

```text
[NAME]Alice[SERVER]Alice joined the chat.[ONLINE]Alice|Bob
```

or only part of one message.

Therefore, the application implements **length-prefix message framing**.

## Protocol

Each message is transmitted as:

```text
[4-byte message length][message bytes]
```

Example:

```text
[5][Hello]
```

The receiver performs:

```text
Read 4 bytes
      ↓
Get message length
      ↓
Read exactly N bytes
      ↓
Decode UTF-8
      ↓
Get complete message
```

This allows the application to determine exactly where one application message ends.

---

# 8. `ReadExactlyAsync`

A single `ReadAsync()` call is not guaranteed to return all requested bytes.

For example, the application may request:

```text
100 bytes
```

but receive:

```text
40 bytes
```

The remaining bytes may arrive later.

Therefore, the application repeatedly reads until the required number of bytes has been received.

Conceptually:

```text
Required: 100 bytes

Read 1 → 40 bytes
Read 2 → 35 bytes
Read 3 → 25 bytes

Total → 100 bytes
```

This is why the project contains:

```csharp
ReadExactlyAsync(...)
```

---

# 9. Application Message Protocol

The current application uses simple text prefixes to distinguish different message types.

## Server assigns username

```text
[NAME]Alice
```

Meaning:

```text
The server has assigned/confirmed the username Alice.
```

## Server notification

```text
[SERVER]Bob joined the chat.
```

Meaning:

```text
System notification
```

## Online user list

```text
[ONLINE]Alice|Bob|Charlie
```

Meaning:

```text
Current online users:
Alice
Bob
Charlie
```

## Chat message

```text
[Alice]Hello Bob!
```

Meaning:

```text
Username = Alice
Message  = Hello Bob!
```

---

# 10. Username Management

The server is responsible for ensuring that usernames are unique.

For example, if the following clients connect:

```text
Alice
Alice
Alice
```

the server assigns:

```text
Alice
Alice2
Alice3
```

This prevents ambiguity when identifying message ownership.

The server is the source of truth for usernames because multiple clients may attempt to use the same username at the same time.

---

# 11. Online Users

The server maintains a list of connected clients.

Each client contains:

```text
TcpClient
Username
```

Conceptually:

```text
ClientInfo
├── Client
└── Username
```

When the online-user list changes, the server broadcasts:

```text
[ONLINE]Alice|Bob|Charlie
```

The clients then update their UI based on the server-provided list.

The client does not independently decide who is online.

---

# 12. Server Workflow

The server starts on port:

```text
5000
```

Main workflow:

```text
Start Server
     │
     ▼
Start TcpListener
     │
     ▼
Wait for client
     │
     ▼
Accept TcpClient
     │
     ▼
Receive username
     │
     ▼
Generate unique username
     │
     ▼
Add client to client list
     │
     ▼
Send [NAME]
     │
     ▼
Broadcast join notification
     │
     ▼
Broadcast online users
     │
     ▼
Receive messages
     │
     ▼
Broadcast messages
     │
     ▼
Client disconnects
     │
     ▼
Remove client
     │
     ▼
Broadcast leave notification
     │
     ▼
Update online users
```

---

# 13. Client Workflow

The client workflow is:

```text
Start WPF Application
        │
        ▼
Enter Username
        │
        ▼
Click Connect
        │
        ▼
TcpClient.ConnectAsync()
        │
        ▼
Send Username
        │
        ▼
Receive Server Messages
        │
        ├── [NAME]
        ├── [ONLINE]
        ├── [SERVER]
        └── [Username]
        │
        ▼
Display messages
```

When the user sends a chat message:

```text
TextBox
   │
   ▼
Send Button / Enter
   │
   ▼
UTF-8 Encoding
   │
   ▼
Length Prefix
   │
   ▼
NetworkStream
   │
   ▼
TCP Server
```

---

# 14. Asynchronous Communication

The application uses asynchronous network APIs such as:

```csharp
AcceptTcpClientAsync()
ConnectAsync()
ReadAsync()
WriteAsync()
```

Network operations may take an unpredictable amount of time.

For example:

```text
Waiting for client
Waiting for message
Waiting for network data
```

Using asynchronous I/O prevents the application from unnecessarily blocking while waiting for network operations.

For the WPF client, this is especially important because blocking the UI thread can cause the interface to become unresponsive.

---

# 15. Client UI Features

The WPF client currently provides:

### Connection

* Connect
* Disconnect
* Connection status

### User

* Username input
* Server-confirmed username
* Online users

### Chat

* Incoming message bubble
* Outgoing message bubble
* Username
* Timestamp
* System messages
* Auto scrolling

### Input

* Send button
* Enter to send
* Emoji popup

---

# 16. Running the Project

## Step 1 — Start Server

Run:

```text
ChatServer
```

Expected output:

```text
=================================
           CHAT SERVER
=================================
Server started on port 5000
Waiting for clients...
```

---

## Step 2 — Start Client

Run:

```text
ChatClient
```

Enter a username, for example:

```text
Alice
```

Click:

```text
Connect
```

The client connects to:

```text
127.0.0.1:5000
```

---

## Step 3 — Start Multiple Clients

Run multiple instances of `ChatClient`.

For example:

```text
Client 1 → Alice
Client 2 → Bob
Client 3 → Charlie
```

Expected online list:

```text
ONLINE

● Alice
● Bob
● Charlie
```

---

# 17. Demonstration Scenario

A recommended Lab 1 demonstration:

### 1. Start server

```text
Server started on port 5000
Waiting for clients...
```

### 2. Connect Alice

```text
Alice joined the chat.
Online clients: 1
```

### 3. Connect Bob

```text
Bob joined the chat.
Online clients: 2
```

Both clients should see:

```text
Alice
Bob
```

### 4. Alice sends

```text
Hello Bob!
```

Bob receives:

```text
Alice
Hello Bob!
```

### 5. Bob replies

```text
Hi Alice!
```

Alice receives:

```text
Bob
Hi Alice!
```

### 6. Disconnect Bob

Alice receives:

```text
Bob left the chat.
```

Online users become:

```text
● Alice
```

---

# 18. Important Networking Concepts

This project demonstrates the following concepts.

## Client–Server

```text
Client → Request/Message → Server
Client ← Response/Broadcast ← Server
```

## IP Address

The client connects to:

```text
127.0.0.1
```

which represents the local machine.

## Port

The server listens on:

```text
5000
```

The combination:

```text
127.0.0.1:5000
```

identifies the server endpoint used by this lab.

## TCP

TCP provides:

```text
Connection
Reliability
Ordering
Byte stream
```

## NetworkStream

`NetworkStream` provides the stream used to read and write data through the TCP connection.

## Encoding

Application strings are converted to bytes using:

```text
UTF-8
```

before being transmitted.

## Message Framing

Length-prefix framing provides application-level message boundaries on top of TCP's byte stream.

---

# 19. Why the Architecture Is Designed This Way

The main design decisions are:

| Decision                | Why                                     |
| ----------------------- | --------------------------------------- |
| TCP                     | Reliable ordered communication          |
| Client–Server           | Centralized chat coordination           |
| `TcpListener`           | Accept incoming TCP connections         |
| `TcpClient`             | Establish/manage TCP client connection  |
| `NetworkStream`         | Read/write TCP bytes                    |
| Async I/O               | Avoid blocking network operations       |
| UTF-8                   | Convert text to bytes                   |
| Length prefix           | Preserve application message boundaries |
| Server-side client list | Maintain shared chat state              |
| Unique username         | Avoid identity conflicts                |
| Server broadcast        | Synchronize clients                     |
| WPF                     | Provide graphical chat interface        |

---

# 20. Key Lessons

The most important lesson from this lab is that:

> **TCP does not transmit application messages. TCP transmits a reliable ordered byte stream.**

Therefore, an application must define its own protocol.

In this project:

```text
Application
     │
     │ Message
     ▼
Message Protocol
     │
     │ Length Prefix
     ▼
UTF-8 Bytes
     │
     ▼
NetworkStream
     │
     ▼
TCP
     │
     ▼
Network
```

Understanding this relationship is more important than simply memorizing the APIs.

---

# 21. Possible Future Improvements

The current implementation is designed for Lab 1. Possible future improvements include:

* JSON-based message models
* Strongly typed message protocol
* Better error handling
* Connection retry
* Server shutdown handling
* Thread-safe client management
* Private messaging
* Chat rooms
* Message history
* Authentication
* Database persistence
* File/image transfer
* Encryption/TLS

These features are outside the current basic Lab 1 implementation.

---

# 22. Learning / Viva Questions

The following questions should be understood before presenting the lab:

### TCP

1. Why did you choose TCP instead of UDP?
2. What does TCP actually provide?
3. Is TCP message-based?
4. What is a TCP byte stream?
5. What happens when a TCP connection is closed?

### Server

6. Why does the server use `TcpListener`?
7. What does `AcceptTcpClientAsync()` do?
8. Why does the server maintain a client list?
9. Why does the server broadcast messages?
10. Why does the server determine the final username?

### Client

11. Why does the client use `TcpClient`?
12. What does `ConnectAsync()` do?
13. Why does the client use `NetworkStream`?
14. Why is the client asynchronous?

### Message Protocol

15. Why is message framing necessary?
16. Why can multiple messages appear in one `ReadAsync()`?
17. Why can one message require multiple reads?
18. Why use a length prefix?
19. Why encode the message using UTF-8?
20. What would happen if framing were removed?

### Architecture

21. Why is the server the source of truth for online users?
22. Why shouldn't each client independently maintain the online-user list?
23. What happens if two clients choose the same username?
24. What happens when a client disconnects unexpectedly?

---

# 23. Current Lab Status

```text
Lab 1 — Chat Client

TCP Server                 ✓
TCP Client                 ✓
Connect / Disconnect       ✓
Send / Receive             ✓
Multiple Clients           ✓
Broadcast                  ✓
Username                   ✓
Join / Leave               ✓
Online Users               ✓
Chat UI                    ✓
Message Bubble             ✓
Auto Scroll                ✓
Enter to Send              ✓
Emoji                      ✓
Message Framing            ✓

JSON Message Model         Planned
Advanced Error Handling    Planned
Final Testing              Planned
```

---

# 24. Final Goal

The goal of this lab is not only to produce a working chat application.

The real goal is to understand the relationship between:

```text
Networking Concept
        ↓
Architecture
        ↓
Protocol
        ↓
.NET API
        ↓
Implementation
        ↓
User Interface
```

A successful implementation should allow the developer to explain not only:

> **"How does this code work?"**

but also:

> **"Why was it designed this way, and what would happen if we changed or removed it?"**

This distinction is especially important when evaluating networking applications.
