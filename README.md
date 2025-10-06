# README - Custom Modifications to S7.Net Library

## Summary of Changes

The S7.Net library has been extended with comprehensive **S7 communication frame logging functionality** for auditing, diagnostic, and forensic analysis purposes. This modification enables automatic capture of all S7 protocol frames exchanged between the client and PLC in hexadecimal format.

## Added Functionality

### 1. FrameDirection Enumeration
New enumeration defining frame direction:
- **Sent**: Frame sent to PLC
- **Received**: Frame received from PLC

### 2. PlcFrame Class
New class representing a communication frame with the following properties:
- **Direction**: Frame direction (FrameDirection)
- **HexData**: Frame data in hexadecimal format (string)
- **Timestamp**: Capture timestamp (DateTime)
- **Operation**: Type of operation performed (string)

### 3. Configuration Properties in Plc Class

#### EnableFrameLogging
- **Type**: bool
- **Default value**: false
- **Description**: Enables or disables automatic frame capture

#### MaxFrameHistorySize
- **Type**: int
- **Default value**: 1000
- **Description**: Maximum size of frame history in memory

### 4. Added Public Methods

#### GetFrameHexCode()
- **Returns**: string
- **Description**: Gets the hexadecimal code of the last captured frame (sent or received)

#### GetFrameHexCode(FrameDirection direction)
- **Parameter**: FrameDirection direction
- **Returns**: string
- **Description**: Gets the hexadecimal code of the last frame of the specified type

#### GetFrameHistory()
- **Returns**: List<PlcFrame>
- **Description**: Gets the complete history of captured frames

#### ClearFrameHistory()
- **Returns**: void
- **Description**: Clears the stored frame history

### 5. Added Private Methods

#### LogFrame(FrameDirection direction, byte[] data, string operation)
- **Description**: Registers a frame in the history with thread-safety

#### TrimFrameHistory()
- **Description**: Maintains history within configured limits

## Technical Implementation

### Automatic Capture
Capture is implemented through interception in communication methods:
- **RequestTsdu()** (synchronous methods) - Modified in PlcSynchronous.cs
- **NoLockRequestTsduAsync()** (asynchronous methods) - Modified in PlcAsynchronous.cs

### Thread Safety
- Uses locks with **_frameHistoryLock** for safe concurrent access
- Circular buffer implemented for efficient memory management

### Memory Management
- Automatic rotation when **MaxFrameHistorySize** is exceeded
- Oldest frames are automatically removed

## Basic Usage

### Initial Configuration
// Enable logging during initialization plc.EnableFrameLogging = true; plc.MaxFrameHistorySize = 500; // Optional, default 1000

### Frame Capture
- Perform read/write operations as usual plc.Read(...); plc.Write(...); 
- Frames are automatically logged if EnableFrameLogging is true
- Get only sent frame string sentFrame = plc.GetFrameHexCode(FrameDirection.Sent);
- Get only received frame string receivedFrame = plc.GetFrameHexCode(FrameDirection.Received);

### History and Cleanup
- Get complete history var history = plc.GetFrameHistory();
- Clear history plc.ClearFrameHistory();

## Important: Frame Content

**WARNING**: The methods do **NOT** return tag values in hexadecimal format. They return the **complete S7 protocol frame** which includes:

- TPKT and COTP headers
- S7 header with function codes
- Addressing information
- Variable data (as part of the frame)
- Response and status codes

### Example of Difference
- **Tag value in hex**: If DB1.DBW100 = 1234, the hex would be "04D2"
- **Complete S7 frame**: "0300001F02F080320100000001000E00000401120A10020001000184000000080004D2"

## Compatibility

### Backward Compatibility
- **100% compatible** with existing code
- Logging is **disabled by default**
- No performance impact when disabled

### Supported Frameworks
- .NET Framework 4.5.2+
- .NET Framework 4.6.2+
- .NET Standard 2.0
- .NET Standard 1.3
- .NET 5+
- .NET 6+
- .NET 7+

## Use Cases

### Communication Auditing
Complete logging of all PLC interactions for regulatory compliance and security audits.

### Problem Diagnosis
Detailed analysis of failed communications through protocol-level frame inspection.

### Development and Debug
Verification of correct S7 communication implementation during application development.

### Forensic Analysis
Reconstruction of communication sequences for incident investigation.

## Performance Considerations

### Performance Impact
- **Minimal** when disabled
- **Low** when enabled (basic synchronous logging)
- **Recommended**: Implement asynchronous logging in critical applications

### Memory Management
- Circular buffer prevents infinite growth
- Configure **MaxFrameHistorySize** according to memory availability
- Clear history periodically in long-running applications

## Modified Files

1. **S7.Net\PLC.cs**: Class definitions
