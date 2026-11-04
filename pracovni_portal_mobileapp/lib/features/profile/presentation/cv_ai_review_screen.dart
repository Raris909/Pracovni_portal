import 'package:flutter/material.dart';

class CvAiReviewScreen extends StatelessWidget {
  const CvAiReviewScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('AI Analýza CV')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(
                children: [
                  Text('Celkové skóre CV', style: Theme.of(context).textTheme.titleMedium),
                  const SizedBox(height: 12),
                  Text(
                    '84 / 100',
                    style: Theme.of(context).textTheme.displayMedium?.copyWith(
                          color: Theme.of(context).colorScheme.primary,
                          fontWeight: FontWeight.bold,
                        ),
                  ),
                  const SizedBox(height: 8),
                  const Text('Stav: Succeeded • Model: gpt-4o-mini (v1.0)'),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          const Card(
            child: ListTile(
              leading: Icon(Icons.check_circle_outline, color: Colors.green),
              title: Text('Silné stránky'),
              subtitle: Text('Jasně strukturované technologie, odkazy na projekty a praxe v oboru.'),
            ),
          ),
          const SizedBox(height: 8),
          const Card(
            child: ListTile(
              leading: Icon(Icons.lightbulb_outline, color: Colors.amber),
              title: Text('Doporučení ke zlepšení'),
              subtitle: Text('Doplňte konkrétní metriky úspěchu u předchozích projektů a certifikace.'),
            ),
          ),
          const SizedBox(height: 24),
          FilledButton.icon(
            icon: const Icon(Icons.refresh),
            label: const Text('Spustit novou AI analýzu'),
            onPressed: () {
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(content: Text('Požadavek zařazen do fronty (status: queued)')),
              );
            },
          ),
        ],
      ),
    );
  }
}