class Conversation {
  final String id;
  final String applicationId;
  final String companyName;
  final String jobTitle;
  final String? lastMessage;
  final DateTime createdAt;

  const Conversation({
    required this.id,
    required this.applicationId,
    required this.companyName,
    required this.jobTitle,
    this.lastMessage,
    required this.createdAt,
  });
}

class ChatMessage {
  final String id;
  final String conversationId;
  final String senderUserId;
  final String body;
  final DateTime sentAt;
  final bool isMe;

  const ChatMessage({
    required this.id,
    required this.conversationId,
    required this.senderUserId,
    required this.body,
    required this.sentAt,
    required this.isMe,
  });
}