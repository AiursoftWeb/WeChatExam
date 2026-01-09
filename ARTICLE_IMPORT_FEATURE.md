# Article Import Feature

This feature allows importing articles and automatically extracting questions using AI (Ollama).

## API Endpoints

### Import Article
```http
POST /api/articles/import
Content-Type: application/json
Authorization: Bearer <token>

{
  "title": "Article Title",
  "content": "Article content to extract questions from...",
  "categoryId": "guid-of-category",
  "tags": ["optional", "tags"],
  "useBackgroundJob": true
}
```

### Extract Questions Only
```http
POST /api/articles/extract-questions
Content-Type: application/json
Authorization: Bearer <token>

{
  "content": "Article content to extract questions from..."
}
```

### Extract Tags Only
```http
POST /api/articles/extract-tags
Content-Type: application/json
Authorization: Bearer <token>

{
  "content": "Article content to extract tags from..."
}
```

## Features

- **AI-Powered Extraction**: Uses Ollama to intelligently extract questions and tags from article content
- **Background Job Processing**: Large imports can be processed asynchronously using the background job system
- **Automatic Tagging**: Extracts relevant tags and applies them to generated questions
- **Multiple Question Types**: Supports Choice, Blank, Bool, ShortAnswer, and Essay question types
- **Flexible Grading**: Supports ExactMatch, FuzzyMatch, and AiEval grading strategies

## Implementation Details

### Services
- `IArticleImportService`: Main service for article import functionality
- `IOllamaService`: AI service for content extraction (already existed)
- `BackgroundJobQueue`: Async job processing (already existed)

### Models
- `ArticleImportDto`: Input data for article import
- `ExtractedQuestionDto`: Structure for extracted questions
- `ArticleImportResultDto`: Import operation results

### Background Processing
The system uses the existing background job infrastructure to process large imports asynchronously:

```csharp
var jobId = await articleImportService.ImportArticleAsync(importDto, userId, useBackgroundJob: true);
```

### AI Prompts
The service uses structured prompts to extract:
- Questions with proper types and metadata
- Relevant tags for categorization
- Standard answers and explanations

## Configuration

Ensure Ollama is properly configured in `appsettings.json`:

```json
{
  "OpenAI": {
    "Token": "your-token",
    "CompletionApiUrl": "http://127.0.0.1:11434/api/chat",
    "Model": "qwen3:30b-a3b-instruct-2507-q8_0"
  }
}
```

## Usage Examples

### Simple Import (Synchronous)
```bash
curl -X POST "https://your-domain/api/articles/import" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Machine Learning Basics",
    "content": "Machine learning is a subset of artificial intelligence...",
    "categoryId": "category-guid",
    "useBackgroundJob": false
  }'
```

### Background Import (Asynchronous)
```bash
curl -X POST "https://your-domain/api/articles/import" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Large Article Collection",
    "content": "Very long article content...",
    "categoryId": "category-guid",
    "useBackgroundJob": true
  }'
```

The background job will process the import and you can track its status using the job ID returned.