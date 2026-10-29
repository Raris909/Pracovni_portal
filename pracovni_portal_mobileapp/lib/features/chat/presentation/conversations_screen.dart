// lib/features/chat/presentation/conversations_screen.dart
import 'package:flutter/material.dart';

class ConversationsScreen extends StatelessWidget {
  const ConversationsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Konverzace s HR')),
      body: ListView(
        children: [
          ListTile(
            leading: const CircleAvatar(child: Icon(Icons.business)),
            title: const Text('TechNova s.r.o. (Flutter Mobile Developer)'),
            subtitle: const Text('Dobrý den, děkujeme za zaslání CV. Hodí se Vám pohovor v úterý?'),
            trailing: const Text('10:45'),
            onTap: () {
              // Navigates to ChatRoomScreen backed by ChatHub.cs (SignalR)
            },
          ),
        ],
      ),
    );
  }
}