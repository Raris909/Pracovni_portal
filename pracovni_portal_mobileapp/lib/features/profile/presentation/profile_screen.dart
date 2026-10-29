import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Můj profil'),
        actions: [
          IconButton(
            icon: const Icon(Icons.logout),
            onPressed: () => context.go('/login'),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const ListTile(
            leading: CircleAvatar(radius: 28, child: Text('JN')),
            title: Text('Jan Novák', style: TextStyle(fontWeight: FontWeight.bold)),
            subtitle: Text('Flutter & .NET Developer • Praha, CZ'),
          ),
          const Divider(height: 32),
          Card(
            child: ListTile(
              leading: const Icon(Icons.picture_as_pdf, color: Colors.red),
              title: const Text('Aktuální CV: Jan_Novak_CV.pdf'),
              subtitle: const Text('Nahráno před 3 dny • 420 KB'),
              trailing: OutlinedButton(
                onPressed: () {
                  // Uses file_picker to upload a new immutable document record
                },
                child: const Text('Nahrát nové'),
              ),
            ),
          ),
          const SizedBox(height: 12),
          FilledButton.tonalIcon(
            icon: const Icon(Icons.auto_awesome),
            label: const Text('AI Hodnocení mého CV'),
            onPressed: () => context.go('/profile/cv-review'),
          ),
          const SizedBox(height: 24),
          Text('Dovednosti (skills)', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          const Wrap(
            spacing: 8,
            children: [
              Chip(label: Text('Flutter')),
              Chip(label: Text('Dart')),
              Chip(label: Text('C#')),
              Chip(label: Text('PostgreSQL')),
            ],
          ),
          const SizedBox(height: 24),
          Text('Pracovní zkušenosti', style: Theme.of(context).textTheme.titleMedium),
          const Card(
            child: ListTile(
              title: Text('Junior Mobile Developer'),
              subtitle: Text('AppWorks s.r.o. • 2024 – Současnost'),
            ),
          ),
          const SizedBox(height: 16),
          Text('Vzdělání', style: Theme.of(context).textTheme.titleMedium),
          const Card(
            child: ListTile(
              title: Text('Softwarové inženýrství (Bc.)'),
              subtitle: Text('Univerzita • 2023 – 2026'),
            ),
          ),
        ],
      ),
    );
  }
}