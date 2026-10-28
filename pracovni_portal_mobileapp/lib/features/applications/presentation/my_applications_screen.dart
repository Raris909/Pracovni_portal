import 'package:flutter/material.dart';

class MyApplicationsScreen extends StatelessWidget {
  const MyApplicationsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Moje přihlášky')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: ExpansionTile(
              title: const Text('Flutter Mobile Developer'),
              subtitle: const Text('TechNova s.r.o. • Stav: Probíhá (in_progress)'),
              children: [
                const ListTile(
                  leading: Icon(Icons.check_circle, color: Colors.green),
                  title: Text('1. Screening CV'),
                  subtitle: Text('Splněno (passed)'),
                ),
                ListTile(
                  leading: const Icon(Icons.schedule, color: Colors.orange),
                  title: const Text('2. Technický pohovor (interview)'),
                  subtitle: const Text('Vyberte si volný termín pohovoru'),
                  trailing: FilledButton.tonal(
                    onPressed: () {
                      // Opens modal to book a slot from portal.interview_slots
                    },
                    child: const Text('Rezervovat termín'),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}