@workspace Read docs/findings/10-response-path-unimplemented.md then implement the fix.

Create src/SSStap/Tunnel/PacketBuilder.cs with BuildTcpResponse() and BuildUdpResponse() that construct valid IPv4+TCP and IPv4+UDP packets for injection into Wintun. Use iOS-Tunnel/iOS_SOCKS5_Proxy/Core/TCP/TCPStateMachine.swift as the design reference for the TCP state machine.

Update TcpConnectionState to track per-flow SeqNum and AckNum. Wire PacketBuilder into TunnelEngine.RelayTcpFromProxyAsync() and ReceiveUdpFromRelayAsync() replacing the current raw SendAsync calls.

Do not change anything in iOS-Tunnel.